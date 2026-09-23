using FluentValidation;
using GameHubz.DataModels.Config;
using GameHubz.DataModels.Consts;
using GameHubz.DataModels.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace GameHubz.Logic.Services
{
    public class MatchService : AppBaseServiceGeneric<MatchEntity, MatchDto, MatchPost, MatchEdit>
    {
        private readonly IStorageService storageService;
        private readonly INotificationService notificationService;
        private readonly TournamentAuthorizationService tournamentAuth;
        private readonly StreamVodResolver streamVodResolver;
        private readonly YouTubeStreamClient youTubeStreamClient;
        private readonly BadgeService badgeService;
        private readonly IDiscordDmService discordDmService;
        private readonly ICacheService cacheService;
        private readonly ShareLinksConfig shareLinksConfig;

        public MatchService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<MatchEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader,
            IStorageService storageService,
            INotificationService notificationService,
            TournamentAuthorizationService tournamentAuth,
            StreamVodResolver streamVodResolver,
            YouTubeStreamClient youTubeStreamClient,
            BadgeService badgeService,
            IDiscordDmService discordDmService,
            ICacheService cacheService,
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
            this.cacheService = cacheService;
            this.shareLinksConfig = shareLinksOptions.Value;
        }

        public async Task<MatchAvailabilityDto> GetAvailability(Guid id, Guid userId)
        {
            var availability = await this.AppUnitOfWork.MatchRepository.GetAvailability(id, userId);
            if (availability == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);
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
            if (detail == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

            // The projection carries the games as raw JSON (EF can't deserialize mid-query);
            // turn them into the parsed lists the client actually consumes.
            detail.Games = DeserializeGames(detail.GamesJson);
            detail.ProposedGames = DeserializeGames(detail.ProposedGamesJson);

            // The projection can only carry the stored columns; the ready-check window around them
            // is arithmetic. Resolved here so the client is handed the same moments the sweep will
            // act on, and the grace it needs to re-derive them after its own check-in.
            if (detail.RequireMatchCheckIn)
            {
                detail.CheckInGraceMinutes = MatchCheckInRules.ResolveGraceMinutes(detail.CheckInGraceMinutes);

                if (detail.ScheduledTime.HasValue && detail.Status == MatchStatus.Scheduled)
                {
                    detail.CheckInOpensAt = MatchCheckInRules.OpensAt(detail.ScheduledTime.Value);
                    detail.CheckInDeadline = MatchCheckInRules.Deadline(
                        detail.ScheduledTime.Value,
                        detail.HomeCheckedInOn,
                        detail.AwayCheckedInOn,
                        detail.CheckInGraceMinutes.Value);
                }
            }

            return detail;
        }

        private static List<SeriesGame>? DeserializeGames(string? json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            // A malformed blob must not take down the whole match screen — the headline score and
            // everything else on the DTO are still valid without the per-game breakdown.
            try { return System.Text.Json.JsonSerializer.Deserialize<List<SeriesGame>>(json); }
            catch { return null; }
        }

        /// <summary>
        /// Fetches this match's streams (empty on any error) alongside the caller's availability
        /// (when the match is still in play — Completed / NoShow returns null). Used by the
        /// /details/full combo endpoint; kept as its own method so the controller can compose the
        /// polymorphic details piece without pulling team logic into MatchService. Takes the
        /// already-loaded match status so we don't re-hit the DB just to check it — the caller
        /// (controller) has the entity in hand.
        /// </summary>
        public async Task<(List<MatchStreamDto> Streams, MatchAvailabilityDto? Availability, MatchAvailabilityAdminDto? AdminAvailability)>
            GetStreamsAndAvailability(Guid id, MatchStatus matchStatus, Guid tournamentId)
        {
            List<MatchStreamDto> streams;
            try { streams = await GetStreams(id); }
            catch { streams = new List<MatchStreamDto>(); }

            MatchAvailabilityDto? availability = null;
            MatchAvailabilityAdminDto? adminAvailability = null;
            if (matchStatus == MatchStatus.Pending || matchStatus == MatchStatus.Scheduled || matchStatus == MatchStatus.Live)
            {
                try
                {
                    var user = await this.UserContextReader.GetTokenUserInfoFromContext();
                    if (user != null)
                    {
                        availability = await GetAvailability(id, user.UserId);

                        // Organizers additionally get both sides named. Caught separately so a
                        // failure here cannot take the caller's OWN availability down with it —
                        // an admin playing their own tournament still needs their picker.
                        try
                        {
                            if (await this.tournamentAuth.CanManageTournamentAsync(tournamentId, user))
                            {
                                adminAvailability = await GetAdminAvailability(id);
                            }
                        }
                        catch { adminAvailability = null; }
                    }
                }
                catch { availability = null; }
            }

            return (streams, availability, adminAvailability);
        }

        /// <summary>
        /// Both sides' scheduling state for an organizer. Unlike <see cref="GetAvailability"/> this
        /// is not caller-relative, so it answers "who answered, when, and do the two lists overlap"
        /// for someone who is playing neither side. Authorization is the caller's to make — the
        /// only entry point today (<see cref="GetStreamsAndAvailability"/>) gates it on
        /// CanManageTournamentAsync.
        /// </summary>
        public async Task<MatchAvailabilityAdminDto?> GetAdminAvailability(Guid id)
        {
            var availability = await this.AppUnitOfWork.MatchRepository.GetAvailabilityForAdmin(id);
            if (availability == null) return null;

            // Intersect here rather than in SQL: the slots live in two JSON columns EF cannot
            // compare mid-query. Ordered so the client can show the earliest mutual hour first —
            // the one SetAvailability would have picked.
            availability.OverlappingSlots = availability.Home.Slots
                .Intersect(availability.Away.Slots)
                .OrderBy(t => t)
                .ToList();

            return availability;
        }

        public async Task<MatchEntity?> GetMatchEntityById(Guid id)
        {
            return await this.AppUnitOfWork.MatchRepository.ShallowGetById(id);
        }

        public async Task<MatchAvailabilityDto> SetAvailability(Guid matchId, List<DateTime> selectedSlots)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId);
            if (match == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

            var outcome = await WithCurrentMatchAsync(match,
                current => SaveAvailabilityAsync(current, user, selectedSlots));
            match = outcome.Match;
            bool isHome = outcome.IsHome;
            bool justScheduled = outcome.JustScheduled;

            // Only when the two lists just met. The offered hours themselves never reach the bracket,
            // but the kick-off and the Scheduled status this branch wrote do — the same cached state
            // ClearSchedule drops on the way back out.
            if (justScheduled)
                await InvalidateBracketCacheAsync(match.TournamentId);

            // A team game that just got a kick-off in a ready-check tournament, with a side that has nobody
            // nominated for it: nobody on that side can check in, so at kick-off + grace the game goes to
            // the other team by forfeit. Only the captain can fix that, and until now nobody told them.
            if (justScheduled && match.TeamMatchId.HasValue && (match.HomeUserId == null || match.AwayUserId == null))
            {
                var settings = await this.AppUnitOfWork.TournamentRepository.GetApprovalContext(match.TournamentId);
                if (settings?.RequireMatchCheckIn == true)
                {
                    var captains = new List<Guid>();
                    if (match.HomeUserId == null && match.HomeParticipant?.Team?.CaptainUserId is Guid homeCaptain) captains.Add(homeCaptain);
                    if (match.AwayUserId == null && match.AwayParticipant?.Team?.CaptainUserId is Guid awayCaptain) captains.Add(awayCaptain);

                    await PushLineupMissingAsync(match, captains, PushText.FromKey("Push.LineupMissing.Body"));
                }
            }

            await NotifyOpponentOfAvailabilityAsync(matchId, user, match, isHome);

            // 4. Return DTO for UI
            return new MatchAvailabilityDto
            {
                MatchId = match.Id!.Value,
                MySlots = isHome ? match.HomeSlots : match.AwaySlots,
                OpponentSlots = isHome ? match.AwaySlots : match.HomeSlots,
                ConfirmedTime = match.ScheduledStartTime,
                MatchDeadline = match.RoundDeadline
            };
        }

        private async Task<(MatchEntity Match, bool IsHome, bool JustScheduled)> SaveAvailabilityAsync(
            MatchEntity match, TokenUserInfo user, List<DateTime> selectedSlots)
        {
            Guid matchId = match.Id!.Value;
            Guid userId = user.UserId;

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

            // A decided match has nothing left to schedule. Without this, two lists that happened to
            // meet on a played or forfeited match flipped it back to Scheduled with its result and
            // advancement still in place.
            ThrowIfMatchDecided(match);

            // 2. Save Slots — normalize to UTC so Intersect() uses consistent DateTimeKind
            var normalizedSlots = selectedSlots
                .Select(s => DateTime.SpecifyKind(s, DateTimeKind.Utc))
                .ToList();

            // Stamped per side, not on the row: BaseEntity.ModifiedOn belongs to whoever wrote the
            // match last (a result, a deadline move) and cannot say which player answered when.
            var submittedOn = DateTime.UtcNow;

            // Only this side's columns — never UpdateEntity. The opponent answers the same picker and
            // the ready check stamps the same row; a full-row write from the snapshot read above would
            // erase whichever of those landed in between. Same serialization as the entity's setter.
            bool saved = await this.AppUnitOfWork.MatchRepository.TrySaveAvailabilitySlots(
                matchId, isHome, System.Text.Json.JsonSerializer.Serialize(normalizedSlots), submittedOn);

            // Re-read after our own write: the intersection must see the opponent's latest hours, not
            // the ones in the snapshot. Whichever of two concurrent answers re-reads last sees both.
            match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

            // The match was decided between our first read and the write.
            if (!saved) ThrowIfMatchDecided(match);

            // 3. CHECK FOR OVERLAP (The Magic)
            // We check if the other person has already picked times
            var mySlots = isHome ? match.HomeSlots : match.AwaySlots;
            var opponentSlots = isHome ? match.AwaySlots : match.HomeSlots;

            // Find slots present in BOTH lists
            var intersection = mySlots.Intersect(opponentSlots).ToList();

            // Only a Pending match gets a kick-off from here. An agreed time stays agreed — the picker
            // is not offered on a scheduled match, and a stale one must not quietly move it (and with
            // it the ready check). Undoing a time is ClearSchedule's job. A tie-break match keeps its
            // status too. The condition is re-checked in the database, so two answers meeting at once
            // schedule the match exactly once.
            bool justScheduled = false;
            if (intersection.Count > 0 && match.Status == MatchStatus.Pending)
            {
                // OrderBy ensures we pick the EARLIEST mutual time (e.g. 10:00 instead of 14:00)
                DateTime kickOff = intersection.OrderBy(t => t).First();

                justScheduled = await this.AppUnitOfWork.MatchRepository
                    .TryScheduleFromAvailability(matchId, kickOff, submittedOn);

                match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId)
                    ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);
            }

            return (match, isHome, justScheduled);
        }

        // A request may have loaded the old pairing while a swap held the tournament lock. Reload
        // after acquiring that same lock, reject a changed pairing, and keep the lock until all
        // writes finish. Using the fresh entity also preserves schedule/check-in changes made while
        // this request waited. Team games include the nominated users in the identity check.
        private async Task<T> WithCurrentMatchAsync<T>(MatchEntity expected, Func<MatchEntity, Task<T>> write)
        {
            await this.AppUnitOfWork.TournamentRepository.AcquireAdvancementLock(expected.TournamentId);
            try
            {
                var current = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(expected.Id!.Value)
                    ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

                if (current.HomeParticipantId != expected.HomeParticipantId
                    || current.AwayParticipantId != expected.AwayParticipantId
                    || current.HomeUserId != expected.HomeUserId
                    || current.AwayUserId != expected.AwayUserId)
                    throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchParticipantsChanged"]);

                return await write(current);
            }
            finally
            {
                await this.AppUnitOfWork.TournamentRepository.ReleaseAdvancementLock(expected.TournamentId);
            }
        }

        private void ThrowIfMatchDecided(MatchEntity match)
        {
            if (match.Status == MatchStatus.Completed)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchAlreadyCompleted"]);

            if (match.Status == MatchStatus.NoShow)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchClosedNoShow"]);
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

            bool scheduled = match.Status == MatchStatus.Scheduled;
            var (title, body) = scheduled
                ? (PushText.FromKey("Push.MatchScheduled.Title"), PushText.FromKey("Push.MatchScheduled.Body", user.Username))
                : (PushText.FromKey("Push.MatchSchedule.Title"), PushText.FromKey("Push.MatchSchedule.Body", user.Username));

            // Sent even without a push token: the opponent still gets it in their inbox. The type only
            // picks the inbox icon and tab — every app version still routes the tap on matchId.
            FireAndForgetPush(
                new List<PushRecipient> { PushRecipient.ForUser(opponentUserId.Value, opponent.PushToken, opponent.Language) },
                title,
                body,
                new { matchId = matchId.ToString(), type = scheduled ? "matchScheduled" : "matchAvailability" });

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
            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

            await WithCurrentMatchAsync(match, async current =>
            {
                // Forcing Scheduled is restricted to the current participants or a tournament admin.
                if (!IsMatchParticipant(current, user.UserId)
                    && !await this.tournamentAuth.CanManageTournamentAsync(current.TournamentId, user))
                    throw new UnauthorizedAccessToServiceException(this.LocalizationService);

                ThrowIfMatchDecided(current);

                // An agreement outside the app is bookkeeping, not a kick-off the ready-check
                // sweep should rule on. Close the check along with the new time, without writing
                // participant ids, results or other fields from this request's snapshot.
                bool saved = await this.AppUnitOfWork.MatchRepository
                    .TrySetScheduled(matchId, DateTime.UtcNow, user.UserId);
                if (!saved)
                    throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchParticipantsChanged"]);

                return true;
            });

            await InvalidateBracketCacheAsync(match.TournamentId);
        }

        /// <summary>
        /// Organizer-only undo of a confirmed kick-off. Both sides' offered hours are dropped along
        /// with the time itself, so the pair start the scheduler from scratch instead of re-confirming
        /// the same overlap the moment one of them touches the picker again (SetAvailability schedules
        /// on the first intersection it finds). The match goes back to Pending — the state it was in
        /// before the two lists met.
        /// </summary>
        public async Task ClearSchedule(Guid matchId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

            match = await WithCurrentMatchAsync(match, async current =>
            {
                // A player cannot cancel an agreed time. Check the current fixture under the lock.
                if (!await this.tournamentAuth.CanManageTournamentAsync(current.TournamentId, user))
                    throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyAdminsClearSchedule"]);

                if (current.Status != MatchStatus.Scheduled)
                    throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotScheduled"]);

                // Both offered lists and all check-in state belong to the cancelled kick-off.
                // Clear only those columns, so unrelated concurrent writes are not overwritten.
                bool saved = await this.AppUnitOfWork.MatchRepository
                    .TryClearSchedule(matchId, DateTime.UtcNow, user.UserId);
                if (!saved)
                    throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotScheduled"]);

                return await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId)
                    ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);
            });

            await InvalidateBracketCacheAsync(match.TournamentId);
            await NotifyScheduleClearedAsync(match);
        }

        /// <summary>
        /// Drops the cached bracket payload for a tournament. Every match write in this service that
        /// the board draws — a kick-off appearing or disappearing, a ready check landing — has to go
        /// through here, because <see cref="BracketService.GetTournamentStructure"/> serves that
        /// payload from a five-minute entry and would otherwise keep showing the state it replaced.
        /// v3 keeps its own key (team group cards are shaped differently), so both are dropped.
        /// </summary>
        private async Task InvalidateBracketCacheAsync(Guid tournamentId)
        {
            await this.cacheService.RemoveByPatternAsync($"bracket:{tournamentId}:*");
            await this.cacheService.RemoveByPatternAsync($"bracket:v3:{tournamentId}:*");
            // Scheduled kick-offs are rendered by the optional schedule PDF variant too.
            await this.cacheService.RemoveByPatternAsync($"pdf:bracket:{tournamentId}:*");
        }

        /// <summary>
        /// Tells both players their time is gone. Without this the pair keep the old kick-off in their
        /// heads while their availability silently empties. Team participants carry no single user id —
        /// those sides are skipped rather than guessed at.
        /// </summary>
        private async Task NotifyScheduleClearedAsync(MatchEntity match)
        {
            var userIds = new[] { GetParticipantUserId(match, isHome: true), GetParticipantUserId(match, isHome: false) }
                .Where(id => id != null)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();

            if (userIds.Count == 0) return;

            var recipients = new List<PushRecipient>();

            // Resolved here, while the request-scoped DbContext is alive — see F109 on the sibling notifier.
            foreach (var userId in userIds)
            {
                var player = await this.AppUnitOfWork.UserRepository.GetById(userId);
                if (player != null)
                    recipients.Add(PushRecipient.ForUser(userId, player.PushToken, player.Language));
            }

            FireAndForgetPush(
                recipients,
                PushText.FromKey("Push.MatchScheduleCleared.Title"),
                PushText.FromKey("Push.MatchScheduleCleared.Body"),
                // tournamentId (and teamMatchId for a team game) let the tap open the match itself, where a
                // new time is picked. Additive: a build that predates them checks matchId first and still
                // lands on My Matches.
                new
                {
                    matchId = match.Id!.Value.ToString(),
                    tournamentId = match.TournamentId.ToString(),
                    teamMatchId = match.TeamMatchId?.ToString(),
                    type = "scheduleCleared",
                });
        }

        /// <summary>
        /// "I am at the keyboard." Stamps the caller's side of a scheduled match so the ready check
        /// can tell who turned up. Idempotent - pressing it again keeps the original stamp, because
        /// that stamp is evidence, not a toggle.
        ///
        /// The window opens <see cref="MatchCheckInRules.OpensBeforeMinutes"/> before kick-off and
        /// closes at the forfeit deadline; after that the sweep owns the match and a late arrival
        /// must not be able to walk into a decision that has already been made. Nothing here awards
        /// anything: checking in alone is not a win, it is what makes the win possible when the
        /// deadline passes with the other side still missing.
        /// </summary>
        public async Task<MatchCheckInDto> CheckIn(Guid matchId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

            var outcome = await WithCurrentMatchAsync(match, current => SaveCheckInAsync(current, user));
            match = outcome.Match;

            if (outcome.Stamped)
            {
                await InvalidateBracketCacheAsync(match.TournamentId);

                // Only the first side's check-in starts the opponent's clock.
                bool opponentWasWaiting = outcome.IsHome
                    ? match.AwayCheckedInOn == null
                    : match.HomeCheckedInOn == null;
                if (opponentWasWaiting)
                    await NotifyOpponentOfCheckInAsync(match, user, outcome.IsHome, outcome.Grace);
            }

            return BuildCheckInDto(match, outcome.Grace, outcome.IsHome);
        }

        private async Task<(MatchEntity Match, int Grace, bool IsHome, bool Stamped)> SaveCheckInAsync(
            MatchEntity match, TokenUserInfo user)
        {
            Guid matchId = match.Id!.Value;
            // GetApprovalContext is the one-query tournament slice the match paths already share;
            // it carries the ready-check settings alongside the approval / series ones.
            var settings = await this.AppUnitOfWork.TournamentRepository.GetApprovalContext(match.TournamentId)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.TournamentNotFound"]);

            if (!settings.RequireMatchCheckIn)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.CheckInNotEnabled"]);

            // No agreed kick-off, no ready check: a pair who arranged the match in chat play and
            // report it exactly as they always have.
            if (match.Status != MatchStatus.Scheduled || !match.ScheduledStartTime.HasValue)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.CheckInNeedsSchedule"]);

            if (match.CheckInResolvedOn.HasValue)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.CheckInClosed"]);

            int grace = MatchCheckInRules.ResolveGraceMinutes(settings.CheckInGraceMinutes);
            DateTime now = DateTime.UtcNow;
            DateTime start = match.ScheduledStartTime.Value;

            if (now < MatchCheckInRules.OpensAt(start))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.CheckInNotOpenYet"]);

            if (now > MatchCheckInRules.Deadline(start, match.HomeCheckedInOn, match.AwayCheckedInOn, grace))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.CheckInClosed"]);

            bool? isHome = ResolveCheckInSide(match, user.UserId);
            if (isHome == null)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.NotAMatchParticipant"]);

            bool alreadyIn = isHome.Value ? match.HomeCheckedInOn.HasValue : match.AwayCheckedInOn.HasValue;

            bool stamped = false;
            if (!alreadyIn)
            {
                // Keep the existing conditional single-column write: the sweep can still decide
                // a fixture, and the opponent's stamp must never be replaced with our snapshot.
                stamped = await this.AppUnitOfWork.MatchRepository
                    .TryStampCheckIn(matchId, isHome.Value, start, now);

                match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId)
                    ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

                bool nowIn = isHome.Value ? match.HomeCheckedInOn.HasValue : match.AwayCheckedInOn.HasValue;
                if (!stamped && !nowIn)
                    throw new BusinessRuleException(this.LocalizationService["BusinessRule.CheckInClosed"]);
            }

            return (match, grace, isHome.Value, stamped);
        }

        /// <summary>
        /// Which side of the match this user may check in for, or null when they are on neither.
        ///
        /// Solo matches are the participant themselves. On a team sub-match the side is the team on
        /// paper, but the check is about a body in a chair, so ONLY the player nominated for THIS
        /// game may answer for it — not a team-mate, and deliberately not the captain. A captain
        /// confirming for someone who is not there would hand his team a forfeit win it did not
        /// turn up for, which is the exact thing the ready check exists to stop. A slot with nobody
        /// nominated cannot be checked in at all: no player, no show.
        /// </summary>
        private static bool? ResolveCheckInSide(MatchEntity match, Guid userId)
        {
            if (match.TeamMatchId.HasValue)
            {
                if (match.HomeUserId == userId) return true;
                if (match.AwayUserId == userId) return false;

                return null;
            }

            if (match.HomeParticipant?.UserId == userId) return true;
            if (match.AwayParticipant?.UserId == userId) return false;

            return null;
        }

        private static MatchCheckInDto BuildCheckInDto(MatchEntity match, int graceMinutes, bool? isHome)
        {
            var dto = new MatchCheckInDto
            {
                MatchId = match.Id!.Value,
                RequireMatchCheckIn = true,
                GraceMinutes = graceMinutes,
                ScheduledStartTime = match.ScheduledStartTime,
                HomeCheckedInOn = match.HomeCheckedInOn,
                AwayCheckedInOn = match.AwayCheckedInOn,
                ResolvedOn = match.CheckInResolvedOn,
                IsHome = isHome
            };

            if (match.ScheduledStartTime.HasValue)
            {
                dto.CheckInOpensAt = MatchCheckInRules.OpensAt(match.ScheduledStartTime.Value);
                dto.CheckInDeadline = MatchCheckInRules.Deadline(
                    match.ScheduledStartTime.Value, match.HomeCheckedInOn, match.AwayCheckedInOn, graceMinutes);
            }

            return dto;
        }

        /// <summary>
        /// Tells the missing side that the clock is running, with the minutes it has left. This is
        /// the notification the whole feature leans on - a forfeit nobody was warned about is just
        /// a match lost to a notification setting. Resolved in the request scope, sent
        /// fire-and-forget, exactly like the availability nudge above.
        /// </summary>
        private async Task NotifyOpponentOfCheckInAsync(MatchEntity match, TokenUserInfo user, bool checkedInHome, int graceMinutes)
        {
            var deadline = MatchCheckInRules.Deadline(
                match.ScheduledStartTime!.Value, match.HomeCheckedInOn, match.AwayCheckedInOn, graceMinutes);

            // Round up: "2 minutes left" reads better than "1" when 1m40s remain, and it is the
            // honest direction to round a deadline the player is racing.
            int minutesLeft = Math.Max(1, (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalMinutes));

            // A team game whose other side has nobody nominated: no player there can answer the ready
            // check, so "you have N minutes to check in" would reach nobody who can press it. What saves
            // the game is a nomination, and only that team's captain can make one.
            if (match.TeamMatchId.HasValue && (checkedInHome ? match.AwayUserId : match.HomeUserId) == null)
            {
                Guid? captainUserId = (checkedInHome ? match.AwayParticipant : match.HomeParticipant)?.Team?.CaptainUserId;

                await PushLineupMissingAsync(
                    match,
                    captainUserId.HasValue ? new[] { captainUserId.Value } : Array.Empty<Guid>(),
                    PushText.FromKey("Push.LineupMissingNow.Body", user.Username, minutesLeft.ToString()));
                return;
            }

            Guid? opponentUserId = GetParticipantUserId(match, isHome: !checkedInHome);
            if (opponentUserId == null) return;

            var opponent = await this.AppUnitOfWork.UserRepository.GetById(opponentUserId.Value);
            if (opponent == null) return;

            // Sent even without a push token: the opponent still gets it in their inbox. tournamentId and
            // teamMatchId let the tap open the match itself — the app's checkIn route needs both ids, and
            // without them it fell back to My Matches.
            FireAndForgetPush(
                new List<PushRecipient> { PushRecipient.ForUser(opponentUserId.Value, opponent.PushToken, opponent.Language) },
                PushText.FromKey("Push.MatchCheckIn.Title"),
                PushText.FromKey("Push.MatchCheckIn.Body", user.Username, minutesLeft.ToString()),
                new
                {
                    matchId = match.Id!.Value.ToString(),
                    tournamentId = match.TournamentId.ToString(),
                    teamMatchId = match.TeamMatchId?.ToString(),
                    type = "checkIn",
                });

            if (opponent.DiscordDmEnabled)
            {
                string dmContent = $"\u2705 **{user.Username}** checked in - you have **{minutesLeft} min** to confirm or you forfeit.\n"
                    + $"[Open in GameHubz](<{shareLinksConfig.BaseUrl}/tournament/{match.TournamentId}>)";
                discordDmService.SendDmInBackground(opponent.DiscordUserId, dmContent);
            }
        }

        // One clip is evidence, several are a video host. Kept low on purpose: video is the only
        // part of evidence whose storage and bandwidth we actually feel.
        private const int MaxVideosPerMatch = 3;

        // Video is enumerated rather than prefix-matched: "video/" must not become a way to park
        // arbitrary files on the account, and these are the containers a phone actually produces.
        // Internal: the verification recording is held to the same list.
        internal static readonly HashSet<string> AllowedVideoTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "video/mp4", "video/quicktime", "video/x-m4v", "video/3gpp", "video/webm"
        };

        /// <summary>
        /// Classifies an upload and rejects anything that is neither picture nor clip.
        ///
        /// This used to be implicit: the storage layer hardcoded an image upload, so the provider
        /// bounced everything else. Now that video is a legitimate answer, the check has to be ours.
        ///
        /// Images stay a prefix match on purpose. Narrowing them to a list would newly reject the
        /// long tail of formats that has always worked (bmp, tiff, whatever a given phone emits),
        /// and the provider still validates the actual bytes on that path — so an explicit list
        /// would buy nothing and break uploads that are fine today.
        /// </summary>
        private EvidenceMediaType ResolveMediaType(IFormFile file)
        {
            string contentType = file.ContentType ?? string.Empty;

            if (AllowedVideoTypes.Contains(contentType)) return EvidenceMediaType.Video;
            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return EvidenceMediaType.Image;

            throw new BusinessRuleException(this.LocalizationService["BusinessRule.EvidenceUnsupportedType"]);
        }

        public async Task UploadMatchEvidence(Guid matchId, List<IFormFile> files)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            // F26: only a match participant or a tournament admin may attach evidence — otherwise any
            // user could pollute an arbitrary match's evidence gallery / burn storage.
            var matchForAuth = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId);
            if (matchForAuth == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

            if (!IsMatchParticipant(matchForAuth, user.UserId) && !await this.tournamentAuth.CanManageTournamentAsync(matchForAuth.TournamentId, user))
            {
                throw new UnauthorizedAccessToServiceException(this.LocalizationService);
            }

            var match = await this.AppUnitOfWork.MatchRepository.GetForMatchEvidence(matchId);
            if (match == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

            // Classify and validate the WHOLE batch before a single byte is uploaded.
            //
            // Rejecting halfway through would leave the files already sent sitting in storage with
            // no row to point at them: the exception skips SaveAsync, so nothing is persisted, and
            // the retention sweep works off rows and could never find them. Everything that can be
            // known up front is therefore decided up front.
            //
            // Videos are capped per match, images are not. One clip settles a dispute; a dozen is
            // somebody using the match as a video host, and unlike screenshots that costs real
            // storage and bandwidth.
            var pending = new List<(IFormFile File, EvidenceMediaType MediaType)>();
            int videoCount = await this.AppUnitOfWork.MatchEvidenceRepository.CountVideosForMatch(matchId);

            foreach (var file in files)
            {
                if (file.Length == 0) continue;

                EvidenceMediaType mediaType = ResolveMediaType(file);

                if (mediaType == EvidenceMediaType.Video && ++videoCount > MaxVideosPerMatch)
                {
                    throw new BusinessRuleException(string.Format(
                        this.LocalizationService["BusinessRule.EvidenceVideoLimit"], MaxVideosPerMatch));
                }

                pending.Add((file, mediaType));
            }

            if (pending.Count == 0) return;

            string folderPath = $"hub/{match.HubName}/tournaments/{match.TournamentName}/matches/{matchId}";
            var uploaded = new List<(StoredAsset Asset, EvidenceMediaType MediaType)>();

            // Upload and persist together, so the guarantee is all-or-nothing: either every
            // uploaded asset has a row pointing at it, or the asset is taken back off storage.
            // A file with no row is invisible to every cleanup path we have — the sweep and the
            // tournament purge both work off rows — so it would sit there being paid for forever.
            try
            {
                foreach (var (file, mediaType) in pending)
                {
                    string fileName = $"evidence_{matchId}_{DateTime.UtcNow.Ticks}";

                    StoredAsset? stored = mediaType == EvidenceMediaType.Video
                        ? await storageService.UploadVideoAsync(file, folderPath, fileName)
                        : await storageService.UploadImageAsync(file, folderPath, fileName);

                    if (stored != null) uploaded.Add((stored, mediaType));
                }

                foreach (var (asset, mediaType) in uploaded)
                {
                    var screenshot = new MatchEvidenceEntity
                    {
                        MatchId = matchId,
                        Url = asset.Url,
                        StorageKey = asset.StorageKey,
                        Provider = asset.Provider,
                        MediaType = mediaType,
                    };

                    await this.AppUnitOfWork.MatchEvidenceRepository.AddEntity(screenshot, this.UserContextReader);
                }

                await this.SaveAsync();
            }
            catch
            {
                // The provider rejected a later file, or the rows failed to save. Take back
                // whatever did land. Best-effort: a failure in here must not replace the error
                // the caller actually needs to see.
                foreach (var (asset, mediaType) in uploaded)
                {
                    try { await storageService.DeleteAsync(asset.StorageKey, mediaType); }
                    catch { /* nothing further to do; the original exception matters more */ }
                }

                throw;
            }

            // 4. Obriši keš (jer se meč promenio)
            // await _cacheService.RemoveAsync($"match:{matchId}");
        }

        public async Task RequestAdminHelp(Guid matchId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId);
            if (match == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

            if (!IsMatchParticipant(match, user.UserId))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyParticipantsRequestHelp"]);

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
                tournament?.Name is { Length: > 0 } tournamentName
                    ? PushText.FromLiteral(tournamentName)
                    : PushText.FromKey("Push.AdminHelp.TitleFallback"),
                PushText.FromKey("Push.AdminHelp.Body", user.Username),
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
            if (match == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

            if (!await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, user))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyAdminsResolveHelp"]);

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

            // Resolve the requester now, while the scope is alive. No push token still means an inbox row.
            var requester = await this.AppUnitOfWork.UserRepository.GetById(requesterUserId.Value);
            if (requester == null) return;

            FireAndForgetPush(
                new List<PushRecipient> { PushRecipient.ForUser(requesterUserId.Value, requester.PushToken, requester.Language) },
                PushText.FromKey("Push.AdminHelpResolved.Title"),
                PushText.FromKey("Push.AdminHelpResolved.Body"),
                // Same ids as scheduleCleared: the tap reopens this match's chat, where the conversation
                // with the organizer happened, instead of dropping the player on My Matches.
                new
                {
                    matchId = matchId.ToString(),
                    tournamentId = match.TournamentId.ToString(),
                    teamMatchId = match.TeamMatchId?.ToString(),
                    type = "adminHelpResolved",
                });
        }

        public async Task<List<MatchAdminHelpItemDto>> GetAdminHelpRequests(Guid tournamentId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            // 400 (not 401) on purpose: the mobile 401-interceptor would otherwise fire a spurious
            // token refresh + retry for a plain authorization failure. BusinessRuleException keeps
            // the descriptive message and stays out of the ErrorLog server-fault noise.
            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId, user))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyAdminsViewHelp"]);

            return await this.AppUnitOfWork.MatchRepository.GetAdminHelpRequests(tournamentId);
        }

        public async Task<List<MatchPendingApprovalItemDto>> GetPendingApprovalMatches(Guid tournamentId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId, user))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyAdminsViewApprovals"]);

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
        private async Task<List<PushRecipient>> CollectHubAdminPushTokensAsync(Guid tournamentId, Guid excludeUserId)
        {
            var ownership = await this.AppUnitOfWork.TournamentRepository.GetHubOwnership(tournamentId);
            if (ownership == null) return new List<PushRecipient>();

            var hubUsers = await this.AppUnitOfWork.UserHubRepository.GetUsersByHub(ownership.HubId);
            // Managers without a push token stay in: they get the inbox row instead of the push.
            var recipients = hubUsers
                .Where(m => (m.HubRole == HubRole.HubOwner || m.HubRole == HubRole.HubAdmin)
                            && m.UserId != excludeUserId)
                .Select(m => PushRecipient.ForUser(m.UserId, m.PushToken, m.Language))
                .ToList();

            // The hub owner may not have a UserHub membership row — include them explicitly.
            if (ownership.OwnerUserId != excludeUserId &&
                !hubUsers.Any(m => m.UserId == ownership.OwnerUserId))
            {
                var owner = await this.AppUnitOfWork.UserRepository.GetById(ownership.OwnerUserId);
                if (owner != null) recipients.Add(PushRecipient.ForUser(ownership.OwnerUserId, owner.PushToken, owner.Language));
            }

            // De-duplicated on the token inside SendLocalizedToManyAsync.
            return recipients;
        }

        // Hands off already-resolved tokens to the push pipeline. Safe inside Task.Run because
        // NotificationService owns its own DbContext scope (see SendBatchAsync).
        /// <summary>
        /// Tells a team's captain that one of their team games has nobody nominated and will be lost by
        /// forfeit if it stays that way. The tap opens the team match, where the lineup is set.
        /// </summary>
        private async Task PushLineupMissingAsync(MatchEntity match, IEnumerable<Guid> captainUserIds, PushText body)
        {
            var recipients = new List<PushRecipient>();

            foreach (Guid captainUserId in captainUserIds.Distinct())
            {
                var captain = await this.AppUnitOfWork.UserRepository.GetById(captainUserId);
                if (captain != null)
                    recipients.Add(PushRecipient.ForUser(captainUserId, captain.PushToken, captain.Language));
            }

            if (recipients.Count == 0) return;

            FireAndForgetPush(
                recipients,
                PushText.FromKey("Push.LineupMissing.Title"),
                body,
                new
                {
                    matchId = match.Id!.Value.ToString(),
                    tournamentId = match.TournamentId.ToString(),
                    teamMatchId = match.TeamMatchId?.ToString(),
                    type = "lineupMissing",
                });
        }

        private void FireAndForgetPush(List<PushRecipient> recipients, PushText title, PushText body, object data)
        {
            if (recipients.Count == 0) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await notificationService.SendLocalizedToManyAsync(recipients, title, body, data);
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
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.UnsupportedStreamPlatform"]);

            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId);
            if (match == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

            if (!IsMatchParticipant(match, user.UserId))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyParticipantsStream"]);

            // Resolve the channel handle: explicit handle wins, otherwise fall back to a saved social.
            var socials = await this.AppUnitOfWork.UserSocialRepository.GetByUserId(user.UserId);
            var existingSocial = socials.FirstOrDefault(s => s.Type == request.Platform);

            var handle = (request.Handle ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(handle))
                handle = existingSocial?.Username?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(handle))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.NoStreamChannel"]);

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
            if (stream == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.NoStreamForMatch"]);

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
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VodUrlRequired"]);

            var targetStreamerId = request.StreamerUserId ?? user.UserId;
            var stream = await this.AppUnitOfWork.MatchStreamRepository.GetLatestByMatchAndStreamer(matchId, targetStreamerId);
            if (stream == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.NoStreamForMatch"]);

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
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyStreamerManageStream"]);
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
