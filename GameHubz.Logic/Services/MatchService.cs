using FluentValidation;
using GameHubz.DataModels.Enums;
using Microsoft.AspNetCore.Http;

namespace GameHubz.Logic.Services
{
    public class MatchService : AppBaseServiceGeneric<MatchEntity, MatchDto, MatchPost, MatchEdit>
    {
        private readonly CloudinaryStorageService storageService;
        private readonly INotificationService notificationService;
        private readonly TournamentAuthorizationService tournamentAuth;
        private readonly StreamVodResolver streamVodResolver;
        private readonly YouTubeStreamClient youTubeStreamClient;

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
            YouTubeStreamClient youTubeStreamClient) : base(
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
        }

        public async Task<MatchAvailabilityDto> GetAvailability(Guid id, Guid userId)
        {
            return await this.AppUnitOfWork.MatchRepository.GetAvailability(id, userId);
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
            return await this.AppUnitOfWork.MatchRepository.GetWithEvidence(id);
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
            if (match == null) throw new Exception("Match not found");

            // 1. Determine side (Home vs Away)
            bool isHome = match.HomeParticipant != null &&
                (match.HomeParticipant.UserId == userId ||
                 match.HomeParticipant.Team?.Members.Any(m => m.UserId == userId) == true);
            bool isAway = match.AwayParticipant != null &&
                (match.AwayParticipant.UserId == userId ||
                 match.AwayParticipant.Team?.Members.Any(m => m.UserId == userId) == true);

            //if (!isHome && !isAway) throw new Exception("User is not a participant in this match");

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

            SendNotification(matchId, user, match, isHome);

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

        private void SendNotification(Guid matchId, TokenUserInfo user, MatchEntity match, bool isHome)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (match.Status == MatchStatus.Scheduled)
                    {
                        var homeUserId = GetParticipantUserId(match, isHome: true);
                        var awayUserId = GetParticipantUserId(match, isHome: false);

                        Guid? opponentUserId = isHome
                            ? GetParticipantUserId(match, isHome: false)
                            : GetParticipantUserId(match, isHome: true);

                        if (opponentUserId == null) return;

                        var opponent = await this.AppUnitOfWork.UserRepository.GetById(opponentUserId.Value);

                        if (opponent == null) return;

                        string body = $"Your match is confirmed vs {user.Username}";

                        if (opponent?.PushToken == null) return;

                        await notificationService.SendToOneAsync(opponent.PushToken, "Match Scheduled", body, new { matchId = matchId.ToString() });
                    }
                    else
                    {
                        Guid? opponentUserId = isHome
                            ? GetParticipantUserId(match, isHome: false)
                            : GetParticipantUserId(match, isHome: true);
                        if (opponentUserId == null) return;

                        var opponent = await this.AppUnitOfWork.UserRepository.GetById(opponentUserId.Value);
                        if (opponent?.PushToken == null) return;

                        await notificationService.SendToOneAsync(
                            opponent.PushToken,
                            "Match schedule",
                            $"{user.Username} set their availability, add yours to confirm a time",
                            new { matchId = matchId.ToString() });
                    }
                }
                catch { /* fire-and-forget */ }
            });
        }

        public async Task SetScheduled(Guid matchId)
        {
            var match = await this.AppUnitOfWork.MatchRepository.ShallowGetById(matchId);
            if (match == null) throw new Exception("Match not found");

            match.ScheduledStartTime = DateTime.UtcNow;
            match.Status = MatchStatus.Scheduled;

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await this.SaveAsync();
        }

        public async Task UploadMatchEvidence(Guid matchId, List<IFormFile> files)
        {
            var match = await this.AppUnitOfWork.MatchRepository.GetForMatchEvidence(matchId);
            if (match == null) throw new Exception("Match not found");

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
            if (match == null) throw new Exception("Match not found");

            if (!IsMatchParticipant(match, user.UserId))
                throw new Exception("Only match participants can request admin help");

            // Idempotent: a second tap must not spam the admins with notifications.
            if (match.AdminHelpRequested) return;

            match.AdminHelpRequested = true;
            match.AdminHelpRequestedByUserId = user.UserId;
            match.AdminHelpRequestedOn = DateTime.UtcNow;

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await this.SaveAsync();

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
                    tournamentId = match.TournamentId.ToString(),
                    type = "adminHelp"
                });
        }

        public async Task ResolveAdminHelp(Guid matchId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var match = await this.AppUnitOfWork.MatchRepository.ShallowGetById(matchId);
            if (match == null) throw new Exception("Match not found");

            if (!await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, user))
                throw new Exception("Only tournament admins can resolve help requests");

            if (!match.AdminHelpRequested) return;

            var requesterUserId = match.AdminHelpRequestedByUserId;
            match.AdminHelpRequested = false;
            match.AdminHelpRequestedByUserId = null;
            match.AdminHelpRequestedOn = null;

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await this.SaveAsync();

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

            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId, user))
                throw new Exception("Only tournament admins can view help requests");

            return await this.AppUnitOfWork.MatchRepository.GetAdminHelpRequests(tournamentId);
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
                throw new Exception("Unsupported streaming platform. Choose Twitch, YouTube or Kick.");

            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId);
            if (match == null) throw new Exception("Match not found");

            if (!IsMatchParticipant(match, user.UserId))
                throw new Exception("Only match participants can stream this match.");

            // Resolve the channel handle: explicit handle wins, otherwise fall back to a saved social.
            var socials = await this.AppUnitOfWork.UserSocialRepository.GetByUserId(user.UserId);
            var existingSocial = socials.FirstOrDefault(s => s.Type == request.Platform);

            var handle = (request.Handle ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(handle))
                handle = existingSocial?.Username?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(handle))
                throw new Exception("No channel found. Add your channel handle to start streaming.");

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
            if (stream == null) throw new Exception("No stream found for this match.");

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
                throw new Exception("VOD URL is required.");

            var targetStreamerId = request.StreamerUserId ?? user.UserId;
            var stream = await this.AppUnitOfWork.MatchStreamRepository.GetLatestByMatchAndStreamer(matchId, targetStreamerId);
            if (stream == null) throw new Exception("No stream found for this match.");

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
                throw new Exception("Only the streamer or a tournament admin can manage this stream.");
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