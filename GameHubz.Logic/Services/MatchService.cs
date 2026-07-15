using FluentValidation;
using GameHubz.DataModels.Config;
using GameHubz.DataModels.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace GameHubz.Logic.Services
{
    public class MatchService : AppBaseServiceGeneric<MatchEntity, MatchDto, MatchPost, MatchEdit>
    {
        private readonly CloudinaryStorageService storageService;
        private readonly INotificationService notificationService;
        private readonly TournamentAuthorizationService tournamentAuth;
        private readonly StreamVodResolver streamVodResolver;
        private readonly YouTubeStreamClient youTubeStreamClient;
        private readonly BadgeService badgeService;
        private readonly IDiscordDmService discordDmService;
        private readonly ShareLinksConfig shareLinksConfig;

        public MatchService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<MatchEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader,
            CloudinaryStorageService storageService,
            INotificationService notificationService,
            TournamentAuthorizationService tournamentAuth,
            StreamVodResolver streamVodResolver,
            YouTubeStreamClient youTubeStreamClient,
            BadgeService badgeService,
            IDiscordDmService discordDmService,
            IOptions<ShareLinksConfig> shareLinksOptions) : base(
                factory.CreateAppUnitOfWork(),
                userContextReader,
                localizationService,
                searchService,
                validator,
                mapper,
                serviceFunctions)
        {
            this.storageService = storageService;
            this.notificationService = notificationService;
            this.tournamentAuth = tournamentAuth;
            this.streamVodResolver = streamVodResolver;
            this.youTubeStreamClient = youTubeStreamClient;
            this.badgeService = badgeService;
            this.discordDmService = discordDmService;
            this.shareLinksConfig = shareLinksOptions.Value;
        }

        public async Task<MatchAvailabilityDto> GetAvailability(Guid id, Guid userId)
        {
            var availability = await this.AppUnitOfWork.MatchRepository.GetAvailability(id, userId);
            if (availability == null) throw new BusinessRuleException("Match not found");
            return availability;
        }

        public async Task<List<MatchOverviewDto>> GetByUser(Guid userId)
        {
            var matches = await this.AppUnitOfWork.MatchRepository.GetByUser(userId);

            // Annotate each match with the user's unread chat count for the per-match badge.
            // Best-effort: never let the badge enrichment break the core match list.
            try
            {
                var matchIds = matches.Select(m => m.Id).ToList();
                var unreadByMatch = await this.AppUnitOfWork.MatchChatRepository.GetUnreadCountsByMatch(matchIds, userId);

                foreach (var match in matches)
                {
                    if (unreadByMatch.TryGetValue(match.Id, out var unread))
                    {
                        match.UnreadMessages = unread;
                    }
                }
            }
            catch
            {
                // unread counts are non-critical — return matches without them on failure
            }

            return matches;
        }

        public async Task<MatchResultDetailDto> GetWithEvidence(Guid id)
        {
            var detail = await this.AppUnitOfWork.MatchRepository.GetWithEvidence(id);
            if (detail == null) throw new BusinessRuleException("Match not found");
            return detail;
        }

        /// <summary>
        /// Fetches this match's streams (empty on any error) alongside the caller's availability
        /// (when the match is still in play — Completed / NoShow returns null). Used by the
        /// /details/full combo endpoint; kept as its own method so the controller can compose the
        /// polymorphic details piece without pulling team logic into MatchService. Takes the
        /// already-loaded match status so we don't re-hit the DB just to check it — the caller
        /// (controller) has the entity in hand.
        /// </summary>
        public async Task<(List<MatchStreamDto> Streams, MatchAvailabilityDto? Availability)> GetStreamsAndAvailability(Guid id, MatchStatus matchStatus)
        {
            List<MatchStreamDto> streams;
            try { streams = await GetStreams(id); }
            catch { streams = new List<MatchStreamDto>(); }

            MatchAvailabilityDto? availability = null;
            if (matchStatus == MatchStatus.Pending || matchStatus == MatchStatus.Scheduled || matchStatus == MatchStatus.Live)
            {
                try
                {
                    var user = await this.UserContextReader.GetTokenUserInfoFromContext();
                    if (user != null)
                    {
                        availability = await GetAvailability(id, user.UserId);
                    }
                }
                catch { availability = null; }
            }

            return (streams, availability);
        }

        public async Task<MatchEntity?> GetMatchEntityById(Guid id)
        {
            return await this.AppUnitOfWork.MatchRepository.ShallowGetById(id);
        }

        public async Task<MatchAvailabilityDto> SetAvailability(Guid matchId, List<DateTime> selectedSlots)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var userId = user.UserId;
            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId);
            if (match == null) throw new BusinessRuleException("Match not found");

            // 1. Determine side (Home vs Away)
            bool isHome = match.HomeParticipant != null &&
                (match.HomeParticipant.UserId == userId ||
                 match.HomeParticipant.Team?.Members.Any(m => m.UserId == userId) == true);
            bool isAway = match.AwayParticipant != null &&
                (match.AwayParticipant.UserId == userId ||
                 match.AwayParticipant.Team?.Members.Any(m => m.UserId == userId) == true);

            // F25: only a participant may set availability. Without this guard a non-participant
            // (isHome=false, isAway=false) silently fell into the else branch below and overwrote the
            // away side's slots on someone else's match.
            if (!isHome && !isAway)
            {
                throw new UnauthorizedAccessToServiceException(this.LocalizationService);
            }

            // 2. Save Slots — normalize to UTC so Intersect() uses consistent DateTimeKind
            var normalizedSlots = selectedSlots
                .Select(s => DateTime.SpecifyKind(s, DateTimeKind.Utc))
                .ToList();

            if (isHome)
            {
                match.HomeSlots = normalizedSlots;
            }
            else
            {
                match.AwaySlots = normalizedSlots;
            }

            // 3. CHECK FOR OVERLAP (The Magic)
            // We check if the other person has already picked times
            var mySlots = isHome ? match.HomeSlots : match.AwaySlots;
            var opponentSlots = isHome ? match.AwaySlots : match.HomeSlots;

            // Find slots present in BOTH lists
            var intersection = mySlots.Intersect(opponentSlots).ToList();

            if (intersection.Count > 0)
            {
                // OrderBy ensures we pick the EARLIEST mutual time (e.g. 10:00 instead of 14:00)
                match.ScheduledStartTime = intersection.OrderBy(t => t).First();
                match.Status = MatchStatus.Scheduled;
            }

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await this.SaveAsync();

            await NotifyOpponentOfAvailabilityAsync(matchId, user, match, isHome);

            // 4. Return DTO for UI
            return new MatchAvailabilityDto
            {
                MatchId = match.Id!.Value,
                MySlots = mySlots,
                OpponentSlots = opponentSlots,
                ConfirmedTime = match.ScheduledStartTime,
                MatchDeadline = match.RoundDeadline
            };
        }

        // F109: the opponent's push token is resolved here (awaited, while the request-scoped DbContext
        // is still alive) and only the push itself is fired-and-forgotten via FireAndForgetPush. The old
        // version queried this.AppUnitOfWork inside Task.Run, which raced/failed against the disposed
        // request-scoped context.
        private async Task NotifyOpponentOfAvailabilityAsync(Guid matchId, TokenUserInfo user, MatchEntity match, bool isHome)
        {
            Guid? opponentUserId = isHome
                ? GetParticipantUserId(match, isHome: false)
                : GetParticipantUserId(match, isHome: true);

            if (opponentUserId == null) return;

            var opponent = await this.AppUnitOfWork.UserRepository.GetById(opponentUserId.Value);
            if (opponent == null) return;

            var (title, body) = match.Status == MatchStatus.Scheduled
                ? ("Match Scheduled", $"Your match is confirmed vs {user.Username}")
                : ("Match schedule", $"{user.Username} set their availability, add yours to confirm a time");

            if (!string.IsNullOrEmpty(opponent.PushToken))
            {
                FireAndForgetPush(
                    new List<string> { opponent.PushToken! },
                    title,
                    body,
                    new { matchId = matchId.ToString() });
            }

            // Additive Discord DM (push stays the primary channel). Same event, same data —
            // resolved here in the request scope, sent fire-and-forget by the DM service.
            // Masked link ([label](<url>)) keeps the raw URL out of the message; the <> also
            // suppresses Discord's link-preview embed.
            if (opponent.DiscordDmEnabled)
            {
                string dmContent = match.Status == MatchStatus.Scheduled
                    ? $"📅 **Match scheduled** — your match vs **{user.Username}** is confirmed.\n[Open in GameHubz](<{shareLinksConfig.BaseUrl}/tournament/{match.TournamentId}>)"
                    : $"🕒 **{user.Username}** set their availability — add yours to confirm a time.\n[Open in GameHubz](<{shareLinksConfig.BaseUrl}/tournament/{match.TournamentId}>)";
                discordDmService.SendDmInBackground(opponent.DiscordUserId, dmContent);
            }
        }

        public async Task SetScheduled(Guid matchId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId);
            if (match == null) throw new BusinessRuleException("Match not found");

            // F31: forcing a match to Scheduled is restricted to its participants or a tournament admin.
            if (!IsMatchParticipant(match, user.UserId) && !await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, user))
            {
                throw new UnauthorizedAccessToServiceException(this.LocalizationService);
            }

            match.ScheduledStartTime = DateTime.UtcNow;
            match.Status = MatchStatus.Scheduled;

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await this.SaveAsync();
        }

        public async Task UploadMatchEvidence(Guid matchId, List<IFormFile> files)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            // F26: only a match participant or a tournament admin may attach evidence — otherwise any
            // user could pollute an arbitrary match's evidence gallery / burn storage.
            var matchForAuth = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId);
            if (matchForAuth == null) throw new BusinessRuleException("Match not found");

            if (!IsMatchParticipant(matchForAuth, user.UserId) && !await this.tournamentAuth.CanManageTournamentAsync(matchForAuth.TournamentId, user))
            {
                throw new UnauthorizedAccessToServiceException(this.LocalizationService);
            }

            var match = await this.AppUnitOfWork.MatchRepository.GetForMatchEvidence(matchId);
            if (match == null) throw new BusinessRuleException("Match not found");

            foreach (var file in files)
            {
                if (file.Length > 0)
                {
                    string fileName = $"evidence_{matchId}_{DateTime.UtcNow.Ticks}";
                    string folderPath = $"hub/{match.HubName}/tournaments/{match.TournamentName}/matches/{matchId}";

                    string url = await storageService.UploadFileAsync(file, folderPath, fileName);

                    var screenshot = new MatchEvidenceEntity
                    {
                        MatchId = matchId,
                        Url = url,
                    };

                    await this.AppUnitOfWork.MatchEvidenceRepository.AddEntity(screenshot, this.UserContextReader);
                }
            }

            await this.SaveAsync();

            // 4. Obriši keš (jer se meč promenio)
            // await _cacheService.RemoveAsync($"match:{matchId}");
        }

        public async Task RequestAdminHelp(Guid matchId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId);
            if (match == null) throw new BusinessRuleException("Match not found");

            if (!IsMatchParticipant(match, user.UserId))
                throw new BusinessRuleException("Only match participants can request admin help");

            // Idempotent: a second tap must not spam the admins with notifications.
            if (match.AdminHelpRequested) return;

            match.AdminHelpRequested = true;
            match.AdminHelpRequestedByUserId = user.UserId;
            match.AdminHelpRequestedOn = DateTime.UtcNow;

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await this.SaveAsync();

            // Managers' "admin help" badge just went up — refresh it live (before the push
            // early-out so it fires even when no manager has a push token configured).
            await this.badgeService.PushToTournamentManagersAsync(match.TournamentId);

            // Gather all recipients + payload while the DbContext is still alive. The actual
            // push call is fire-and-forget below so it must NOT touch this scope's DbContext.
            var pushTokens = await CollectHubAdminPushTokensAsync(match.TournamentId, excludeUserId: user.UserId);
            if (pushTokens.Count == 0) return;

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetById(match.TournamentId);

            FireAndForgetPush(
                pushTokens,
                tournament?.Name ?? "Admin help needed",
                $"{user.Username} requested admin help in their match.",
                new
                {
                    matchId = match.Id!.Value.ToString(),
                    // Carried for team-tournament sub-matches so the mobile deep link can route
                    // to the team-match modal (the solo modal renders empty for a sub-match id).
                    teamMatchId = match.TeamMatchId?.ToString(),
                    tournamentId = match.TournamentId.ToString(),
                    type = "adminHelp"
                });
        }

        public async Task ResolveAdminHelp(Guid matchId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var match = await this.AppUnitOfWork.MatchRepository.ShallowGetById(matchId);
            if (match == null) throw new BusinessRuleException("Match not found");

            if (!await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, user))
                throw new BusinessRuleException("Only tournament admins can resolve help requests");

            if (!match.AdminHelpRequested) return;

            var requesterUserId = match.AdminHelpRequestedByUserId;
            match.AdminHelpRequested = false;
            match.AdminHelpRequestedByUserId = null;
            match.AdminHelpRequestedOn = null;

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await this.SaveAsync();

            // Managers' "admin help" badge just dropped — refresh it live.
            await this.badgeService.PushToTournamentManagersAsync(match.TournamentId);

            if (requesterUserId == null) return;

            // Resolve the requester's push token now, while the scope is alive.
            var requester = await this.AppUnitOfWork.UserRepository.GetById(requesterUserId.Value);
            if (string.IsNullOrEmpty(requester?.PushToken)) return;

            FireAndForgetPush(
                new List<string> { requester.PushToken! },
                "Help request resolved",
                "An admin reviewed your match and marked the issue as resolved.",
                new { matchId = matchId.ToString(), type = "adminHelpResolved" });
        }

        public async Task<List<MatchAdminHelpItemDto>> GetAdminHelpRequests(Guid tournamentId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            // 400 (not 401) on purpose: the mobile 401-interceptor would otherwise fire a spurious
            // token refresh + retry for a plain authorization failure. BusinessRuleException keeps
            // the descriptive message and stays out of the ErrorLog server-fault noise.
            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId, user))
                throw new BusinessRuleException("Only tournament admins can view help requests");

            return await this.AppUnitOfWork.MatchRepository.GetAdminHelpRequests(tournamentId);
        }

        public async Task<List<MatchPendingApprovalItemDto>> GetPendingApprovalMatches(Guid tournamentId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId, user))
                throw new BusinessRuleException("Only tournament admins can view pending approvals");

            return await this.AppUnitOfWork.MatchRepository.GetPendingApprovalMatches(tournamentId);
        }

        private static bool IsMatchParticipant(MatchEntity match, Guid userId)
        {
            // Team sub-matches carry the player ids on the match itself; solo matches use the participants.
            if (match.HomeUserId == userId || match.AwayUserId == userId) return true;

            return (match.HomeParticipant != null &&
                        (match.HomeParticipant.UserId == userId ||
                         match.HomeParticipant.Team?.Members.Any(m => m.UserId == userId) == true)) ||
                   (match.AwayParticipant != null &&
                        (match.AwayParticipant.UserId == userId ||
                         match.AwayParticipant.Team?.Members.Any(m => m.UserId == userId) == true));
        }

        // Resolves every push token entitled to "Admin help" notifications for a tournament:
        // hub owner + hub admins, plus the hub owner row if they aren't in UserHub.
        // Called while the request-scoped DbContext is alive — never from a Task.Run.
        private async Task<List<string>> CollectHubAdminPushTokensAsync(Guid tournamentId, Guid excludeUserId)
        {
            var ownership = await this.AppUnitOfWork.TournamentRepository.GetHubOwnership(tournamentId);
            if (ownership == null) return new List<string>();

            var hubUsers = await this.AppUnitOfWork.UserHubRepository.GetUsersByHub(ownership.HubId);
            var pushTokens = hubUsers
                .Where(m => (m.HubRole == HubRole.HubOwner || m.HubRole == HubRole.HubAdmin)
                            && m.UserId != excludeUserId
                            && !string.IsNullOrEmpty(m.PushToken))
                .Select(m => m.PushToken!)
                .ToList();

            // The hub owner may not have a UserHub membership row — include them explicitly.
            if (ownership.OwnerUserId != excludeUserId &&
                !hubUsers.Any(m => m.UserId == ownership.OwnerUserId))
            {
                var owner = await this.AppUnitOfWork.UserRepository.GetById(ownership.OwnerUserId);
                if (!string.IsNullOrEmpty(owner?.PushToken)) pushTokens.Add(owner.PushToken!);
            }

            return pushTokens.Distinct().ToList();
        }

        // Hands off already-resolved tokens to the push pipeline. Safe inside Task.Run because
        // NotificationService owns its own DbContext scope (see SendBatchAsync).
        private void FireAndForgetPush(List<string> pushTokens, string title, string body, object data)
        {
            if (pushTokens.Count == 0) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await notificationService.SendToManyAsync(pushTokens, title, body, data);
                }
                catch { /* fire-and-forget */ }
            });
        }

        private static Guid? GetParticipantUserId(MatchEntity match, bool isHome) =>
            isHome
                ? (match.HomeUserId ?? match.HomeParticipant?.UserId)
                : (match.AwayUserId ?? match.AwayParticipant?.UserId);

        // ─────────────────────────────────────────────────────────────────────
        //  Match streaming
        // ─────────────────────────────────────────────────────────────────────

        // One-tap "I'm streaming this match". Resolves the channel from the explicit handle or the
        // user's saved socials, persists it back to socials for next time, and marks the stream Live.
        public async Task<MatchStreamDto> StartStream(Guid matchId, StartMatchStreamRequest request)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            if (!IsStreamingPlatform(request.Platform))
                throw new BusinessRuleException("Unsupported streaming platform. Choose Twitch, YouTube or Kick.");

            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId);
            if (match == null) throw new BusinessRuleException("Match not found");

            if (!IsMatchParticipant(match, user.UserId))
                throw new BusinessRuleException("Only match participants can stream this match.");

            // Resolve the channel handle: explicit handle wins, otherwise fall back to a saved social.
            var socials = await this.AppUnitOfWork.UserSocialRepository.GetByUserId(user.UserId);
            var existingSocial = socials.FirstOrDefault(s => s.Type == request.Platform);

            var handle = (request.Handle ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(handle))
                handle = existingSocial?.Username?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(handle))
                throw new BusinessRuleException("No channel found. Add your channel handle to start streaming.");

            // Persist the channel on the profile for next time (create or update).
            if (existingSocial == null)
            {
                await this.AppUnitOfWork.UserSocialRepository.AddEntity(new UserSocialEntity
                {
                    Type = request.Platform,
                    Username = handle,
                    UserId = user.UserId
                }, this.UserContextReader);
            }
            else if (!string.Equals(existingSocial.Username, handle, StringComparison.OrdinalIgnoreCase))
            {
                existingSocial.Username = handle;
                await this.AppUnitOfWork.UserSocialRepository.UpdateEntity(existingSocial, this.UserContextReader);
            }

            // YouTube LIVE embeds (live_stream?channel=UC..) only work with a stable channel id, not
            // @handles. Resolve once at start (time-boxed) so the embed Just Works in-app and the link
            // survives any future @handle rename. The human-readable handle stays on the user's social.
            var embedHandle = handle;
            if (request.Platform == SocialType.YouTube)
            {
                using var ytCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var channelId = await this.youTubeStreamClient.TryResolveChannelIdAsync(handle, ytCts.Token);
                if (!string.IsNullOrWhiteSpace(channelId)) embedHandle = channelId!;
            }

            // Reuse this streamer's own live row; otherwise create a new one.
            // Both opponents can stream at once — each owns a separate MatchStream row.
            var stream = await this.AppUnitOfWork.MatchStreamRepository.GetLatestByMatchAndStreamer(matchId, user.UserId);
            if (stream != null && stream.Status == MatchStreamStatus.Live)
            {
                stream.Platform = request.Platform;
                stream.ChannelHandle = embedHandle;
                stream.StartedAt ??= DateTime.UtcNow;
                await this.AppUnitOfWork.MatchStreamRepository.UpdateEntity(stream, this.UserContextReader);
            }
            else
            {
                stream = new MatchStreamEntity
                {
                    MatchId = matchId,
                    StreamerUserId = user.UserId,
                    Platform = request.Platform,
                    ChannelHandle = embedHandle,
                    Status = MatchStreamStatus.Live,
                    StartedAt = DateTime.UtcNow,
                };
                await this.AppUnitOfWork.MatchStreamRepository.AddEntity(stream, this.UserContextReader);
            }

            await this.SaveAsync();

            return await ToDtoAsync(stream);
        }

        // Explicitly triggered by the streamer when they stop. Marks Ended and resolves the VOD link
        // ONCE from the platform API (time-boxed). A manual VodUrl in the request overrides resolution
        // (also the Kick fallback path). No background jobs.
        public async Task<MatchStreamDto> EndStream(Guid matchId, EndMatchStreamRequest? request)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            // Target the caller's own stream by default; an admin may pass StreamerUserId to end another's.
            var targetStreamerId = request?.StreamerUserId ?? user.UserId;
            var stream = await this.AppUnitOfWork.MatchStreamRepository.GetLatestByMatchAndStreamer(matchId, targetStreamerId);
            if (stream == null) throw new BusinessRuleException("No stream found for this match.");

            await EnsureCanManageStream(stream, matchId, user);

            stream.Status = MatchStreamStatus.Ended;
            stream.EndedAt ??= DateTime.UtcNow;

            // All three platforms (Twitch / YouTube / Kick) auto-resolve via their official APIs using
            // the channel handle stored on this row. Frontend never prompts; an explicit request.VodUrl
            // only acts as an admin override.
            var vod = request?.VodUrl?.Trim();

            if (string.IsNullOrWhiteSpace(vod) && IsStreamingPlatform(stream.Platform))
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var startedAt = stream.StartedAt ?? stream.CreatedOn ?? stream.EndedAt!.Value;
                vod = await this.streamVodResolver.ResolveVodUrlAsync(
                    stream.Platform, stream.ChannelHandle, startedAt, stream.EndedAt!.Value, cts.Token);
            }

            if (!string.IsNullOrWhiteSpace(vod))
                stream.VodUrl = vod;

            await this.AppUnitOfWork.MatchStreamRepository.UpdateEntity(stream, this.UserContextReader);
            await this.SaveAsync();

            return await ToDtoAsync(stream);
        }

        // Manual VOD link — the silent fallback when auto-resolution couldn't find one (mainly Kick),
        // or an admin/streamer correction after the fact.
        public async Task<MatchStreamDto> SetStreamVod(Guid matchId, SetMatchStreamVodRequest request)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            if (string.IsNullOrWhiteSpace(request.VodUrl))
                throw new BusinessRuleException("VOD URL is required.");

            var targetStreamerId = request.StreamerUserId ?? user.UserId;
            var stream = await this.AppUnitOfWork.MatchStreamRepository.GetLatestByMatchAndStreamer(matchId, targetStreamerId);
            if (stream == null) throw new BusinessRuleException("No stream found for this match.");

            await EnsureCanManageStream(stream, matchId, user);

            stream.VodUrl = request.VodUrl.Trim();
            if (stream.Status != MatchStreamStatus.Ended)
            {
                stream.Status = MatchStreamStatus.Ended;
                stream.EndedAt ??= DateTime.UtcNow;
            }

            await this.AppUnitOfWork.MatchStreamRepository.UpdateEntity(stream, this.UserContextReader);
            await this.SaveAsync();

            return await ToDtoAsync(stream);
        }

        // Removes a stream entirely (soft-delete). Use when the streamer attached the wrong channel
        // or ended by mistake and wants to start fresh. Idempotent: a missing row is a no-op.
        public async Task DeleteStream(Guid matchId, Guid? streamerUserId = null)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var targetStreamerId = streamerUserId ?? user.UserId;

            var stream = await this.AppUnitOfWork.MatchStreamRepository.GetLatestByMatchAndStreamer(matchId, targetStreamerId);
            if (stream == null) return;

            await EnsureCanManageStream(stream, matchId, user);

            await this.AppUnitOfWork.MatchStreamRepository.SoftDeleteEntity(stream, this.UserContextReader);
            await this.SaveAsync();
        }

        // Latest stream for a match, regardless of streamer (kept for back-compat / single-stream callers).
        public async Task<MatchStreamDto?> GetStream(Guid matchId)
        {
            var stream = await this.AppUnitOfWork.MatchStreamRepository.GetLatestByMatchId(matchId);
            if (stream == null) return null;

            await EnsurePlayableKickVodAsync(stream);
            return await ToDtoAsync(stream);
        }

        // All current streams for a match — the latest row per streamer, so both opponents show up.
        // Live streams sort first. Drives the dual-stream Stream tab in the app.
        public async Task<List<MatchStreamDto>> GetStreams(Guid matchId)
        {
            var all = await this.AppUnitOfWork.MatchStreamRepository.GetByMatchId(matchId);

            var latestPerStreamer = all
                .GroupBy(s => s.StreamerUserId)
                .Select(g => g.First()) // GetByMatchId is newest-first
                .OrderByDescending(s => s.Status == MatchStreamStatus.Live)
                .ThenByDescending(s => s.StartedAt)
                .ToList();

            var result = new List<MatchStreamDto>();
            foreach (var s in latestPerStreamer)
            {
                await EnsurePlayableKickVodAsync(s);
                result.Add(await ToDtoAsync(s));
            }

            return result;
        }

        // Kick VODs are only embeddable as a direct .m3u8. Rows created before that change (or ended
        // before Kick finished processing the VOD) hold a non-playable link — the watch page or the
        // old kick.com/video/{uuid}. When such a row is read back, try once more to resolve the real
        // manifest and persist it, so the replay becomes playable in-app without any user action.
        private async Task EnsurePlayableKickVodAsync(MatchStreamEntity stream)
        {
            if (stream.Platform != SocialType.Kick) return;
            if (stream.Status != MatchStreamStatus.Ended) return;
            if (!string.IsNullOrWhiteSpace(stream.VodUrl) &&
                stream.VodUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var startedAt = stream.StartedAt ?? stream.CreatedOn ?? stream.EndedAt ?? DateTime.UtcNow;
                var endedAt = stream.EndedAt ?? DateTime.UtcNow;

                var vod = await this.streamVodResolver.ResolveVodUrlAsync(
                    stream.Platform, stream.ChannelHandle, startedAt, endedAt, cts.Token);

                // Only persist a real manifest — never downgrade to the (non-playable) watch-page fallback.
                if (!string.IsNullOrWhiteSpace(vod) &&
                    vod.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) &&
                    vod != stream.VodUrl)
                {
                    stream.VodUrl = vod;
                    await this.AppUnitOfWork.MatchStreamRepository.UpdateEntity(stream, this.UserContextReader);
                    await this.SaveAsync();
                }
            }
            catch
            {
                // Best-effort; the panel still offers the manual paste / open-channel fallback.
            }
        }

        // Only the streamer or a tournament admin may end/edit a stream.
        private async Task EnsureCanManageStream(MatchStreamEntity stream, Guid matchId, TokenUserInfo user)
        {
            if (stream.StreamerUserId == user.UserId) return;

            var match = await this.AppUnitOfWork.MatchRepository.ShallowGetById(matchId);
            if (match == null || !await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, user))
                throw new BusinessRuleException("Only the streamer or a tournament admin can manage this stream.");
        }

        private static bool IsStreamingPlatform(SocialType platform) =>
            platform == SocialType.Twitch || platform == SocialType.YouTube || platform == SocialType.Kick;

        private async Task<MatchStreamDto> ToDtoAsync(MatchStreamEntity stream)
        {
            var streamer = await this.AppUnitOfWork.UserRepository.ShallowGetById(stream.StreamerUserId);
            return MapStreamDto(stream, streamer);
        }

        private static MatchStreamDto MapStreamDto(MatchStreamEntity stream, UserEntity? streamer)
        {
            return new MatchStreamDto
            {
                Id = stream.Id ?? Guid.Empty,
                MatchId = stream.MatchId,
                StreamerUserId = stream.StreamerUserId,
                StreamerUsername = streamer?.Username,
                StreamerNickname = streamer?.Nickname,
                StreamerAvatarUrl = streamer?.AvatarUrl,
                Platform = stream.Platform,
                ChannelHandle = stream.ChannelHandle,
                ChannelUrl = BuildChannelUrl(stream.Platform, stream.ChannelHandle),
                Status = stream.Status,
                VodUrl = stream.VodUrl,
                VodPending = stream.Status == MatchStreamStatus.Ended && string.IsNullOrWhiteSpace(stream.VodUrl),
                StartedAt = stream.StartedAt,
                EndedAt = stream.EndedAt,
            };
        }

        private static string BuildChannelUrl(SocialType platform, string handle)
        {
            var h = (handle ?? string.Empty).Trim().TrimStart('@');

            // If the user stored a full url, hand it back as-is.
            if (h.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                h.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return h;

            return platform switch
            {
                SocialType.Twitch => $"https://www.twitch.tv/{h}",
                SocialType.YouTube => h.StartsWith("UC", StringComparison.Ordinal)
                    ? $"https://www.youtube.com/channel/{h}"
                    : $"https://www.youtube.com/@{h}",
                SocialType.Kick => $"https://kick.com/{h}",
                _ => string.Empty,
            };
        }

        protected override IRepository<MatchEntity> GetRepository()
            => this.AppUnitOfWork.MatchRepository;
    }
}

//