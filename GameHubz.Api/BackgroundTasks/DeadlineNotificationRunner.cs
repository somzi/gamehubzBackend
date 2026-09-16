using GameHubz.Common.Consts;
using GameHubz.Common.Interfaces;
using GameHubz.Data.Context;
using GameHubz.DataModels.Config;
using GameHubz.DataModels.Consts;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameHubz.Api.BackgroundTasks
{
    /// <summary>
    /// One pass of the deadline sweeps, run on its own DI scope by
    /// <see cref="DeadlineNotificationTask"/>. Reads straight off ApplicationContext (like
    /// ShareController) and marks each handled row with ExecuteUpdate so a row is never
    /// re-evaluated on the next tick — that marker is what keeps every push one-shot.
    /// Also opens tournaments whose scheduled registration time has arrived; there the status
    /// flip itself is the marker.
    /// </summary>
    public class DeadlineNotificationRunner
    {
        // How far back the ready-check sweep will rule. A verdict is about a kick-off that has
        // just been and gone; past this the fixture is history an organizer has to settle by hand.
        // Two things depend on it: an organizer who switches the ready check ON mid-tournament must
        // not have every long-stale scheduled fixture voided in the next tick, and an API that was
        // down for a day must not come back and mass-forfeit what it slept through.
        private const int CheckInRulingWindowHours = 24;

        // The last-call lead is capped at RoundFinalLeadTimeMinutes but shrinks to this fraction
        // of the round length for short rounds, so it always lands after the round opens (a fixed
        // 3h last-call would never fire on a 1-hour round — its players would get nothing).
        private const double RoundFinalLeadFraction = 0.4;

        private readonly ApplicationContext context;
        private readonly INotificationService notificationService;
        private readonly IDiscordDmService discordDmService;
        private readonly ILocalizationService localizationService;
        private readonly TournamentNotifier tournamentNotifier;
        private readonly BracketService bracketService;
        private readonly ICacheService cacheService;
        private readonly ShareLinksConfig shareLinksConfig;
        private readonly ILogger<DeadlineNotificationRunner> logger;
        private readonly int registrationLeadMinutes;
        private readonly int roundEarlyLeadMinutes;
        private readonly int roundFinalLeadMinutes;
        private readonly int maxMatchesPerSweep;

        public DeadlineNotificationRunner(
            ApplicationContext context,
            INotificationService notificationService,
            IDiscordDmService discordDmService,
            ILocalizationService localizationService,
            TournamentNotifier tournamentNotifier,
            BracketService bracketService,
            ICacheService cacheService,
            IOptions<ShareLinksConfig> shareLinksOptions,
            IConfiguration configuration,
            ILogger<DeadlineNotificationRunner> logger)
        {
            this.context = context;
            this.notificationService = notificationService;
            this.discordDmService = discordDmService;
            this.localizationService = localizationService;
            this.tournamentNotifier = tournamentNotifier;
            this.bracketService = bracketService;
            this.cacheService = cacheService;
            this.shareLinksConfig = shareLinksOptions.Value;
            this.logger = logger;

            // Registration: a single last-call reminder this many minutes before the deadline.
            this.registrationLeadMinutes = configuration.GetValue(
                "BackgroundTasks:DeadlineNotificationTask:RegistrationLeadTimeMinutes", 180);

            // Round: an early reminder (only for rounds long enough to warrant it) plus an
            // adaptive last-call. Defaults: 24h early, 3h last-call cap.
            this.roundEarlyLeadMinutes = configuration.GetValue(
                "BackgroundTasks:DeadlineNotificationTask:RoundEarlyLeadTimeMinutes", 1440);
            this.roundFinalLeadMinutes = configuration.GetValue(
                "BackgroundTasks:DeadlineNotificationTask:RoundFinalLeadTimeMinutes", 180);

            // Fixtures one tick of the check-in and opponent-ready sweeps will take on. Whatever is left
            // over is picked up a minute later; unbounded, a mid-tournament switch-on or an outage
            // turned a single tick into hundreds of serial rulings while every other sweep waited.
            this.maxMatchesPerSweep = Math.Clamp(configuration.GetValue(
                "BackgroundTasks:DeadlineNotificationTask:MaxMatchesPerSweep", 200), 1, 2000);
        }

        public async Task RunAsync(CancellationToken ct)
        {
            // First: tournaments whose opening time has arrived become open. Running it ahead of the
            // closing-reminder sweep means a tournament with a very short registration window can be
            // opened and reminded in the same tick rather than a minute apart.
            try
            {
                await SweepScheduledRegistrationOpeningsAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled registration-opening sweep failed.");
            }

            try
            {
                await SweepRegistrationDeadlinesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Registration-deadline sweep failed.");
            }

            try
            {
                await SweepRoundDeadlinesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Round-deadline sweep failed.");
            }

            try
            {
                await SweepCheckInDeadlinesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Check-in deadline sweep failed.");
            }

            try
            {
                await SweepOpponentReadyAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Opponent-ready sweep failed.");
            }
        }

        // Scheduled opening: a tournament created with a RegistrationOpensAt was saved as a Draft —
        // out of the feed, closed to sign-ups — and this is what publishes it. The Draft →
        // RegistrationOpen flip is the whole mechanism: every registration path (solo, team, and
        // the feed's AvailableToJoin filter) already gates on that status. The announcement fires
        // here rather than at creation so followers hear about the tournament at the moment they
        // can actually join it, which is the point of scheduling one.
        private async Task SweepScheduledRegistrationOpeningsAsync(CancellationToken ct)
        {
            DateTime now = DateTime.UtcNow;

            var due = await context.Set<TournamentEntity>()
                .AsNoTracking()
                .Where(t => t.Status == TournamentStatus.Draft
                    && t.RegistrationOpensAt != null
                    && t.RegistrationOpensAt <= now
                    && t.HubId != null)
                .ToListAsync(ct);

            foreach (var tournament in due)
            {
                if (ct.IsCancellationRequested) return;

                try
                {
                    // The flip doubles as the claim: with the Draft guard in the WHERE, a second API
                    // instance sweeping the same tick updates 0 rows and skips the announcement, so
                    // nobody gets the push twice.
                    int claimed = await context.Set<TournamentEntity>()
                        .Where(t => t.Id == tournament.Id && t.Status == TournamentStatus.Draft)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(t => t.Status, TournamentStatus.RegistrationOpen)
                            .SetProperty(t => t.ModifiedOn, now), ct);

                    if (claimed == 0) continue;

                    await LogRegistrationOpenedAsync(tournament.HubId!.Value, tournament.Id!.Value, now, ct);

                    // The same caches TournamentService flushes on a manual open — the overview and
                    // every hub listing page still carry the pre-open status until they are dropped.
                    await cacheService.RemoveAsync($"tournament:{tournament.Id}");
                    await cacheService.RemoveAsync($"hub_overview:{tournament.HubId}");
                    await cacheService.RemoveByPatternAsync($"tournaments:hub:{tournament.HubId}:*");

                    // Expo push to the hub's members + the hub's Discord announcement, identical to
                    // the manual Open Registration transition. Swallows its own failures.
                    await tournamentNotifier.RegistrationOpened(tournament);

                    logger.LogInformation(
                        "Scheduled registration opened for tournament {TournamentId}.", tournament.Id);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed scheduled opening for tournament {TournamentId}.", tournament.Id);
                }
            }
        }

        // HubActivityService is unusable from here: it stamps CreatedBy off the HTTP user context,
        // which a background scope doesn't have. Written straight to the context under the same
        // system id AnonymousUserContextReader uses for unattended work, with the timestamps the
        // unit of work would otherwise have stamped.
        private async Task LogRegistrationOpenedAsync(Guid hubId, Guid tournamentId, DateTime now, CancellationToken ct)
        {
            context.Set<HubActivityEntity>().Add(new HubActivityEntity
            {
                Id = Guid.NewGuid(),
                HubId = hubId,
                TournamentId = tournamentId,
                Type = HubActivityType.RegistrationOpen,
                CreatedOn = now,
                ModifiedOn = now,
                CreatedBy = SystemUsers.AppAdminUserId,
                ModifiedBy = SystemUsers.AppAdminUserId,
            });

            await context.SaveChangesAsync(ct);
        }

        // "Registration closes soon" → eligible hub members who have NOT registered yet.
        // Solo tournaments only: team tournaments register via captain-led teams, so an
        // individual "you haven't registered" nudge would be misleading there.
        private async Task SweepRegistrationDeadlinesAsync(CancellationToken ct)
        {
            DateTime now = DateTime.UtcNow;
            DateTime windowEnd = now.AddMinutes(this.registrationLeadMinutes);

            var due = await context.Set<TournamentEntity>()
                .AsNoTracking()
                .Where(t => t.Status == TournamentStatus.RegistrationOpen
                    && t.IsTeamTournament == false
                    && t.HubId != null
                    && t.RegistrationDeadline != null
                    && t.RegistrationDeadline > now
                    && t.RegistrationDeadline <= windowEnd
                    && t.RegistrationDeadlineReminderSentOn == null)
                .Select(t => new
                {
                    Id = t.Id!.Value,
                    t.Name,
                    HubId = t.HubId!.Value,
                    t.IsExclusive,
                })
                .ToListAsync(ct);

            foreach (var tournament in due)
            {
                if (ct.IsCancellationRequested) return;

                try
                {
                    var memberQuery = context.Set<UserHubEntity>()
                        .AsNoTracking()
                        .Where(uh => uh.HubId == tournament.HubId && uh.UserId != null);

                    // Exclusive tournaments are visible/joinable only to Exclusive-or-higher roles,
                    // so only those members are worth nudging.
                    if (tournament.IsExclusive)
                    {
                        memberQuery = memberQuery.Where(uh =>
                            uh.HubRole == HubRole.HubExclusive
                            || uh.HubRole == HubRole.HubAdmin
                            || uh.HubRole == HubRole.HubOwner);
                    }

                    var memberIds = await memberQuery
                        .Select(uh => uh.UserId!.Value)
                        .Distinct()
                        .ToListAsync(ct);

                    if (memberIds.Count == 0)
                    {
                        await MarkRegistrationRemindedAsync(tournament.Id, now, ct);
                        continue;
                    }

                    // Anyone with a still-standing (non-rejected) registration is already in.
                    var registeredIds = await context.Set<TournamentRegistrationEntity>()
                        .AsNoTracking()
                        .Where(r => r.TournamentId == tournament.Id
                            && r.UserId != null
                            && r.Status != TournamentRegistrationStatus.Rejected)
                        .Select(r => r.UserId!.Value)
                        .ToListAsync(ct);

                    var targetIds = memberIds.Except(registeredIds).ToList();

                    if (targetIds.Count > 0)
                    {
                        // Every eligible member with whatever channels they have: the inbox always, a push
                        // when there is a token, and a linked Discord DM on top.
                        var targets = await context.Set<UserEntity>()
                            .AsNoTracking()
                            .Where(u => targetIds.Contains(u.Id!.Value) && u.IsActive)
                            .Select(u => new { Id = u.Id!.Value, u.PushToken, u.Language, u.DiscordUserId, u.DiscordDmEnabled })
                            .ToListAsync(ct);

                        var recipients = targets
                            .Select(t => PushRecipient.ForUser(t.Id, t.PushToken, t.Language))
                            .ToList();

                        if (recipients.Count > 0)
                        {
                            await notificationService.SendLocalizedToManyAsync(
                                recipients,
                                PushText.FromLiteral(tournament.Name),
                                PushText.FromKey("Push.RegistrationClosingSoon.Body"),
                                new { tournamentId = tournament.Id, type = "registrationDeadline" });
                        }

                        foreach (var target in targets.Where(t => t.DiscordUserId != null && t.DiscordDmEnabled))
                        {
                            // Built per target: this runs outside any request, so the recipient's
                            // own language is the only thing that can decide the wording.
                            string dmContent = $"⏰ **{tournament.Name}** — {this.localizationService["Dm.RegistrationClosingSoon.Body", target.Language]}\n"
                                + $"[{this.localizationService["Dm.OpenInApp", target.Language]}](<{shareLinksConfig.BaseUrl}/tournament/{tournament.Id}>)";
                            await discordDmService.SendDmAsync(target.DiscordUserId!, dmContent);
                        }
                    }

                    // Mark sent even when nobody was eligible, so this tournament is not
                    // re-scanned on every tick until its deadline passes.
                    await MarkRegistrationRemindedAsync(tournament.Id, now, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed registration reminder for tournament {TournamentId}.", tournament.Id);
                }
            }
        }

        // "Your match deadline is approaching" → the players of a match that is still unplayed.
        // Pending/Scheduled/Live with no result proposal means neither side has played it yet.
        // Two adaptive waves: an early (24h) reminder for long-enough rounds, then a last-call
        // that is capped at 3h but shrinks for short rounds so it always fires after the round opens.
        private async Task SweepRoundDeadlinesAsync(CancellationToken ct)
        {
            DateTime now = DateTime.UtcNow;
            // The early lead is the widest window we ever act in; the last-call lead is always smaller.
            DateTime windowEnd = now.AddMinutes(this.roundEarlyLeadMinutes);

            var due = await context.Set<MatchEntity>()
                .AsNoTracking()
                .Where(m => m.RoundDeadline != null
                    && m.RoundDeadline > now
                    && m.RoundDeadline <= windowEnd
                    && m.RoundReminderStage < 2
                    && m.ProposedByUserId == null
                    && (m.RoundOpenAt == null || m.RoundOpenAt <= now)
                    && (m.Status == MatchStatus.Pending
                        || m.Status == MatchStatus.Scheduled
                        || m.Status == MatchStatus.Live)
                    && m.Tournament!.Status == TournamentStatus.InProgress)
                // Bounded like the other match sweeps; nearest deadline first.
                .OrderBy(m => m.RoundDeadline)
                .Take(maxMatchesPerSweep)
                .Select(m => new
                {
                    Id = m.Id!.Value,
                    m.TournamentId,
                    m.TeamMatchId,
                    TournamentName = m.Tournament!.Name,
                    Deadline = m.RoundDeadline!.Value,
                    m.RoundOpenAt,
                    m.RoundReminderStage,
                    RoundDurationMinutes = m.Tournament!.RoundDurationMinutes,
                    // Team sub-matches carry the player ids directly; solo matches go via participants.
                    HomeUserId = m.HomeUserId ?? (m.HomeParticipant != null ? m.HomeParticipant.UserId : null),
                    AwayUserId = m.AwayUserId ?? (m.AwayParticipant != null ? m.AwayParticipant.UserId : null),
                })
                .ToListAsync(ct);

            // Each match's wave is decided first and the tick then acts on all of them together: one user
            // query, one inbox write and a shared run of Expo requests, and one marker update per wave —
            // where every match used to pay for its own user lookup, full send and marker update.
            var reminders = new List<RoundReminder>();

            foreach (var match in due)
            {
                // Round length drives the adaptive last-call: prefer the actual open→deadline
                // span, fall back to the tournament's configured round duration, else unknown.
                double? roundLengthMinutes =
                    match.RoundOpenAt.HasValue ? (match.Deadline - match.RoundOpenAt.Value).TotalMinutes
                    : match.RoundDurationMinutes.HasValue ? match.RoundDurationMinutes.Value
                    : (double?)null;

                double finalLeadMinutes = roundLengthMinutes.HasValue
                    ? Math.Min(this.roundFinalLeadMinutes, roundLengthMinutes.Value * RoundFinalLeadFraction)
                    : this.roundFinalLeadMinutes;

                DateTime lastCallAt = match.Deadline.AddMinutes(-finalLeadMinutes);
                DateTime earlyAt = match.Deadline.AddMinutes(-this.roundEarlyLeadMinutes);

                // Early reminder only makes sense when 24h-before lands after the round opened;
                // shorter rounds skip straight to the single last-call.
                bool earlyEligible = roundLengthMinutes.HasValue
                    && roundLengthMinutes.Value >= this.roundEarlyLeadMinutes;

                if (match.RoundReminderStage < 1 && earlyEligible && now >= earlyAt && now < lastCallAt)
                {
                    reminders.Add(new RoundReminder(match.Id, 1, "Push.RoundDeadline.Body", match.TournamentId,
                        match.TournamentName, match.TeamMatchId, match.HomeUserId, match.AwayUserId));
                }
                else if (now >= lastCallAt)
                {
                    // Covers both the normal last-call and the case where we missed the early
                    // window (task was down) — we jump straight to the final reminder, never both.
                    reminders.Add(new RoundReminder(match.Id, 2, "Push.RoundDeadlineFinal.Body", match.TournamentId,
                        match.TournamentName, match.TeamMatchId, match.HomeUserId, match.AwayUserId));
                }

                // Otherwise: not inside any reminder window yet.
            }

            if (reminders.Count == 0 || ct.IsCancellationRequested) return;

            // Both players with whatever channels they have: the inbox always, a push when there is a
            // token, and a linked Discord DM on top.
            var users = await LoadSweepUsersAsync(reminders.SelectMany(r => new[] { r.HomeUserId, r.AwayUserId }), ct);

            var pushes = reminders
                .Select(r => new LocalizedPush(
                    RecipientsOf(users, new[] { r.HomeUserId, r.AwayUserId }),
                    PushText.FromLiteral(r.TournamentName),
                    PushText.FromKey(r.BodyKey),
                    // teamMatchId — carried for team-tournament sub-matches so the mobile deep link can
                    // route to the team-match modal (the solo modal renders empty for a sub-match id).
                    new { tournamentId = r.TournamentId, matchId = r.MatchId, teamMatchId = r.TeamMatchId, type = "roundDeadline" }))
                .Where(push => push.Recipients.Count > 0)
                .ToList();

            try
            {
                await notificationService.SendLocalizedBatchAsync(pushes);
            }
            catch (Exception ex)
            {
                // Nothing is marked, so the whole tick is retried on the next one — the same rule a single
                // failed match used to follow.
                logger.LogWarning(ex, "Failed round reminders for {Count} match(es).", reminders.Count);
                return;
            }

            // The inbox/push wave above is the primary notification. Persist its one-shot marker
            // before the additive Discord fan-out: if the process dies halfway through hundreds of
            // DMs, the next tick must not send the primary wave and the completed DMs again. A crash
            // after this point may omit some optional DMs, but every player already has the durable
            // inbox row (and push where configured), which is preferable to duplicate reminders.
            //
            // Deliberately NOT cancellable: the pushes are already out, so a shutdown landing in this
            // window would otherwise drop the marker and make the next start resend the whole wave —
            // the exact duplicate this ordering exists to prevent. It is one bounded UPDATE per wave.
            foreach (var wave in reminders.GroupBy(r => r.Stage))
            {
                int stage = wave.Key;
                var matchIds = wave.Select(r => r.MatchId).ToList();

                await context.Set<MatchEntity>()
                    .Where(m => matchIds.Contains(m.Id!.Value))
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.RoundReminderStage, stage), CancellationToken.None);
            }

            foreach (var reminder in reminders)
            {
                foreach (Guid? userId in new[] { reminder.HomeUserId, reminder.AwayUserId })
                {
                    if (userId == null
                        || !users.TryGetValue(userId.Value, out var target)
                        || !target.IsActive
                        || target.DiscordUserId == null
                        || !target.DiscordDmEnabled)
                    {
                        continue;
                    }

                    try
                    {
                        string dmContent = $"⏰ **{reminder.TournamentName}** — {this.localizationService[reminder.BodyKey, target.Language]}\n"
                            + $"[{this.localizationService["Dm.OpenInApp", target.Language]}](<{shareLinksConfig.BaseUrl}/tournament/{reminder.TournamentId}>)";
                        await discordDmService.SendDmAsync(target.DiscordUserId, dmContent);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed round reminder DM for match {MatchId}.", reminder.MatchId);
                    }
                }
            }
        }

        /// <summary>
        /// Rules the ready check on every scheduled match whose grace period has run out.
        ///
        ///   • one side checked in  → that side wins by forfeit, scored as a walkover of the
        ///     match's own Best-of (Bo1 1-0, Bo3 2-0, Bo5 3-0)
        ///   • neither did          → the fixture is voided as a double walkover, exactly as the
        ///     organizer's own button would (both out of an elimination, a no-points NoShow in a
        ///     league / group / Swiss, a nothing-game inside a team tie)
        ///   • both did             → nothing to rule; they are playing
        ///
        /// The ruling itself lives in BracketService so a forfeit advances the bracket, resyncs
        /// standings and re-aggregates a team tie like any other result. Everything it does is
        /// reversible by an organizer: delete the awarded result and enter the real one.
        /// </summary>
        private async Task SweepCheckInDeadlinesAsync(CancellationToken ct)
        {
            DateTime now = DateTime.UtcNow;

            // Kick-off already passed is the cheap pre-filter — the real deadline is kick-off plus
            // the grace (or later, if the side that did turn up was itself late), so it can only
            // ever be after this. The exact arithmetic runs per row below.
            DateTime oldestRulable = now.AddHours(-CheckInRulingWindowHours);

            var due = await context.Set<MatchEntity>()
                .AsNoTracking()
                .Where(m => m.Status == MatchStatus.Scheduled
                    && m.ScheduledStartTime != null
                    && m.ScheduledStartTime <= now
                    && m.ScheduledStartTime >= oldestRulable
                    && m.CheckInResolvedOn == null
                    && m.ProposedByUserId == null
                    && (m.HomeCheckedInOn == null || m.AwayCheckedInOn == null)
                    && m.Tournament!.RequireMatchCheckIn
                    && m.Tournament!.Status == TournamentStatus.InProgress)
                // Oldest kick-off first, so a backlog is ruled in the order it fell due.
                .OrderBy(m => m.ScheduledStartTime)
                .Take(maxMatchesPerSweep)
                .Select(m => new
                {
                    Id = m.Id!.Value,
                    m.TournamentId,
                    TournamentName = m.Tournament!.Name,
                    m.TeamMatchId,
                    Start = m.ScheduledStartTime!.Value,
                    m.HomeCheckedInOn,
                    m.AwayCheckedInOn,
                    GraceMinutes = m.Tournament!.CheckInGraceMinutes,
                    // Team sub-matches carry the player ids directly; solo matches go via participants.
                    HomeUserId = m.HomeUserId ?? (m.HomeParticipant != null ? m.HomeParticipant.UserId : null),
                    AwayUserId = m.AwayUserId ?? (m.AwayParticipant != null ? m.AwayParticipant.UserId : null),
                })
                .ToListAsync(ct);

            // Everyone the rulings below may notify, in one query — and only for the rows whose deadline
            // has actually passed, so a tick where everyone is still inside their grace queries nothing.
            var users = await LoadSweepUsersAsync(
                due.Where(m => now >= MatchCheckInRules.Deadline(
                        m.Start, m.HomeCheckedInOn, m.AwayCheckedInOn, MatchCheckInRules.ResolveGraceMinutes(m.GraceMinutes)))
                    .SelectMany(m => new[] { m.HomeUserId, m.AwayUserId }),
                ct);

            foreach (var match in due)
            {
                if (ct.IsCancellationRequested) return;

                try
                {
                    int grace = MatchCheckInRules.ResolveGraceMinutes(match.GraceMinutes);
                    DateTime deadline = MatchCheckInRules.Deadline(
                        match.Start, match.HomeCheckedInOn, match.AwayCheckedInOn, grace);

                    if (now < deadline) continue;

                    bool homeIn = match.HomeCheckedInOn != null;
                    bool awayIn = match.AwayCheckedInOn != null;

                    if (homeIn || awayIn)
                    {
                        if (!await bracketService.ApplyCheckInForfeit(match.Id, homeWins: homeIn)) continue;

                        logger.LogInformation(
                            "Check-in forfeit awarded on match {MatchId} to the {Side} side.",
                            match.Id, homeIn ? "home" : "away");

                        await NotifyCheckInForfeitAsync(
                            match.TournamentId,
                            match.TournamentName,
                            match.Id,
                            match.TeamMatchId,
                            winnerUserId: homeIn ? match.HomeUserId : match.AwayUserId,
                            loserUserId: homeIn ? match.AwayUserId : match.HomeUserId,
                            users,
                            ct);
                    }
                    else
                    {
                        if (!await bracketService.ApplyCheckInDoubleWalkover(match.Id)) continue;

                        logger.LogInformation("Check-in double walkover applied on match {MatchId}.", match.Id);

                        await NotifyCheckInVoidAsync(
                            match.TournamentId,
                            match.TournamentName,
                            match.Id,
                            match.TeamMatchId,
                            new[] { match.HomeUserId, match.AwayUserId },
                            users,
                            ct);
                    }
                }
                catch (Exception ex)
                {
                    // The claim inside the ruling is what stops a failure here from being retried
                    // every tick; the fixture is left for the organizer instead.
                    logger.LogWarning(ex, "Failed check-in ruling for match {MatchId}.", match.Id);
                }
            }
        }

        // Winner and loser hear different things, so this is two sends rather than one broadcast.
        private async Task NotifyCheckInForfeitAsync(
            Guid tournamentId, string tournamentName, Guid matchId, Guid? teamMatchId,
            Guid? winnerUserId, Guid? loserUserId, IReadOnlyDictionary<Guid, SweepUser> users, CancellationToken ct)
        {
            await SendCheckInPushAsync(tournamentId, tournamentName, matchId, teamMatchId,
                new[] { winnerUserId }, "Push.MatchCheckInWin.Body", users, ct);

            await SendCheckInPushAsync(tournamentId, tournamentName, matchId, teamMatchId,
                new[] { loserUserId }, "Push.MatchCheckInLoss.Body", users, ct);
        }

        private Task NotifyCheckInVoidAsync(
            Guid tournamentId, string tournamentName, Guid matchId, Guid? teamMatchId,
            IEnumerable<Guid?> userIds, IReadOnlyDictionary<Guid, SweepUser> users, CancellationToken ct)
            => SendCheckInPushAsync(tournamentId, tournamentName, matchId, teamMatchId,
                userIds, "Push.MatchCheckInVoid.Body", users, ct);

        private async Task SendCheckInPushAsync(
            Guid tournamentId, string tournamentName, Guid matchId, Guid? teamMatchId,
            IEnumerable<Guid?> userIds, string bodyKey, IReadOnlyDictionary<Guid, SweepUser> users, CancellationToken ct)
        {
            var recipients = RecipientsOf(users, userIds);

            if (recipients.Count == 0) return;

            await notificationService.SendLocalizedToManyAsync(
                recipients,
                PushText.FromLiteral(tournamentName),
                PushText.FromKey(bodyKey),
                // teamMatchId rides along so a sub-match deep link opens the tie, not an empty
                // solo modal — same contract as the round-deadline push.
                new { tournamentId, matchId, teamMatchId, type = "checkIn" });
        }

        /// <summary>
        /// Tells both players that a fixture which had an empty slot now has an opponent in it.
        ///
        /// This is the gap the app had: a player knocked out of the group stage into a knockout
        /// draw, or sitting on round 5 of a Swiss waiting for somebody else's match to finish, had
        /// no way of learning that their next match had become playable short of opening the
        /// bracket and checking. The fixture would sit there until the round deadline reminder
        /// eventually mentioned it.
        ///
        /// Only fixtures whose opponent was genuinely unknown qualify — an elimination bracket, a
        /// Swiss pairing, a play-in. League and group fixtures are drawn in full the day the stage
        /// is generated, so announcing them would be telling players something they have been
        /// looking at all along. Round 1 of the opening stage is skipped for the same reason: that
        /// is the tournament starting, which already has its own announcement.
        ///
        /// Deliberately a sweep rather than a hook on the advancement code. A pairing can be
        /// completed from a dozen places — bracket advancement, losers-bracket drop-in, knockout
        /// seeding out of groups or Swiss, a grand-final reset, the third-place play-off, a play-in
        /// feed, an organizer editing a result and the cascade re-draw that follows. Reading the
        /// finished state once a minute catches all of them, including the ones written next year.
        /// </summary>
        private async Task SweepOpponentReadyAsync(CancellationToken ct)
        {
            DateTime now = DateTime.UtcNow;

            var due = await context.Set<MatchEntity>()
                .AsNoTracking()
                .Where(m => m.OpponentNotifiedOn == null
                    && m.Status == MatchStatus.Pending
                    && m.HomeParticipantId != null
                    && m.AwayParticipantId != null
                    && m.WinnerParticipantId == null
                    // A round the organizer has scheduled for next week is not news yet; the round
                    // reminders own that conversation.
                    && (m.RoundOpenAt == null || m.RoundOpenAt <= now)
                    && m.Tournament!.Status == TournamentStatus.InProgress
                    // Fixtures that were drawn, not scheduled up front.
                    && m.TournamentStage!.Type != StageType.League
                    && m.TournamentStage!.Type != StageType.GroupStage
                    // The opening round of the opening stage IS the tournament starting, and
                    // Push.TournamentLive already says so.
                    && !(m.TournamentStage!.Order == 1 && m.RoundNumber == 1))
                // Bounded per tick like the check-in sweep; the marker drops each announced row out of
                // this query, so whatever did not fit is simply first in line next tick.
                .OrderBy(m => m.Id)
                .Take(maxMatchesPerSweep)
                .Select(m => new
                {
                    Id = m.Id!.Value,
                    m.TournamentId,
                    TournamentName = m.Tournament!.Name,
                    m.TeamMatchId,
                    // Team sub-matches carry the player ids directly; solo matches go via participants.
                    HomeUserId = m.HomeUserId ?? (m.HomeParticipant != null ? m.HomeParticipant.UserId : null),
                    AwayUserId = m.AwayUserId ?? (m.AwayParticipant != null ? m.AwayParticipant.UserId : null),
                    HomeTeamName = m.HomeParticipant != null && m.HomeParticipant.Team != null
                        ? m.HomeParticipant.Team.TeamName : null,
                    AwayTeamName = m.AwayParticipant != null && m.AwayParticipant.Team != null
                        ? m.AwayParticipant.Team.TeamName : null,
                    HomeCaptainUserId = m.HomeParticipant != null && m.HomeParticipant.Team != null
                        ? m.HomeParticipant.Team.CaptainUserId : null,
                    AwayCaptainUserId = m.AwayParticipant != null && m.AwayParticipant.Team != null
                        ? m.AwayParticipant.Team.CaptainUserId : null,
                })
                .ToListAsync(ct);

            // Names and push targets for every player and captain in this tick, in one query — this used
            // to be four user lookups per announced match.
            var users = await LoadSweepUsersAsync(
                due.SelectMany(m => new[] { m.HomeUserId, m.AwayUserId, m.HomeCaptainUserId, m.AwayCaptainUserId }),
                ct);

            // Team ties: one announcement per tie, to the two captains. The games of a tie carry the
            // teams from the moment it is drawn, but a player only once their captain nominates them —
            // so a per-game send found nobody to tell, stamped the marker anyway, and the news was lost
            // for good. The captain is the person who has to act on it (the lineup is next), and the
            // nominated players hear about their games from the lineup push. Every game of the tie is
            // claimed at once, so a best-of-five tie is one push, not five.
            foreach (var tie in due.Where(m => m.TeamMatchId != null).GroupBy(m => m.TeamMatchId!.Value))
            {
                if (ct.IsCancellationRequested) return;

                var game = tie.First();

                try
                {
                    int claimed = await context.Set<MatchEntity>()
                        .Where(m => m.TeamMatchId == tie.Key && m.OpponentNotifiedOn == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.OpponentNotifiedOn, now), ct);

                    if (claimed == 0) continue;

                    await SendOpponentReadyPushAsync(game.TournamentId, game.TournamentName, game.Id,
                        tie.Key, game.HomeCaptainUserId, game.AwayTeamName ?? "—", users, ct);

                    await SendOpponentReadyPushAsync(game.TournamentId, game.TournamentName, game.Id,
                        tie.Key, game.AwayCaptainUserId, game.HomeTeamName ?? "—", users, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed opponent-ready notification for team match {TeamMatchId}.", tie.Key);
                }
            }

            foreach (var match in due.Where(m => m.TeamMatchId == null))
            {
                if (ct.IsCancellationRequested) return;

                try
                {
                    // Claimed before the sends, not after: a push that fails must not put the
                    // fixture back in the queue for another try a minute later, and a second API
                    // instance sweeping the same tick updates no rows and stands down.
                    int claimed = await context.Set<MatchEntity>()
                        .Where(m => m.Id == match.Id && m.OpponentNotifiedOn == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.OpponentNotifiedOn, now), ct);

                    if (claimed == 0) continue;

                    // Each player hears their OWN opponent's name, so this is two sends rather than
                    // one broadcast. A team side is named by its team; a solo side by its username.
                    string homeName = match.HomeTeamName ?? UsernameOf(users, match.HomeUserId);
                    string awayName = match.AwayTeamName ?? UsernameOf(users, match.AwayUserId);

                    await SendOpponentReadyPushAsync(match.TournamentId, match.TournamentName, match.Id,
                        match.TeamMatchId, match.HomeUserId, awayName, users, ct);

                    await SendOpponentReadyPushAsync(match.TournamentId, match.TournamentName, match.Id,
                        match.TeamMatchId, match.AwayUserId, homeName, users, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed opponent-ready notification for match {MatchId}.", match.Id);
                }
            }
        }

        /// <summary>A user as the match sweeps need them: a name, a push target and a Discord DM target.</summary>
        private sealed record SweepUser(
            Guid Id, string? Username, string? PushToken, string? Language, bool IsActive, string? DiscordUserId, bool DiscordDmEnabled);

        /// <summary>One round-deadline reminder the tick has decided to send.</summary>
        private sealed record RoundReminder(
            Guid MatchId, int Stage, string BodyKey, Guid TournamentId, string TournamentName,
            Guid? TeamMatchId, Guid? HomeUserId, Guid? AwayUserId);

        // One query for everyone a sweep may notify or name, instead of one or two per match.
        private async Task<Dictionary<Guid, SweepUser>> LoadSweepUsersAsync(IEnumerable<Guid?> userIds, CancellationToken ct)
        {
            var ids = userIds.Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<Guid, SweepUser>();

            return await context.Set<UserEntity>()
                .AsNoTracking()
                .Where(u => ids.Contains(u.Id!.Value))
                .Select(u => new SweepUser(u.Id!.Value, u.Username, u.PushToken, u.Language, u.IsActive, u.DiscordUserId, u.DiscordDmEnabled))
                .ToDictionaryAsync(u => u.Id, ct);
        }

        // Deactivated accounts still have a name to show, so the lookup above does not filter them out.
        private static string UsernameOf(IReadOnlyDictionary<Guid, SweepUser> users, Guid? userId)
            => userId != null && users.TryGetValue(userId.Value, out var user) && !string.IsNullOrWhiteSpace(user.Username)
                ? user.Username!
                : "—";

        // No push token is no reason to skip anyone — they still get the inbox row. A deactivated account is.
        private static List<PushRecipient> RecipientsOf(IReadOnlyDictionary<Guid, SweepUser> users, IEnumerable<Guid?> userIds)
            => userIds
                .Where(id => id != null)
                .Select(id => id!.Value)
                .Distinct()
                .Select(id => users.TryGetValue(id, out var user) && user.IsActive ? user : null)
                .Where(user => user != null)
                .Select(user => PushRecipient.ForUser(user!.Id, user.PushToken, user.Language))
                .ToList();

        private async Task SendOpponentReadyPushAsync(
            Guid tournamentId, string tournamentName, Guid matchId, Guid? teamMatchId,
            Guid? recipientUserId, string opponentName, IReadOnlyDictionary<Guid, SweepUser> users, CancellationToken ct)
        {
            var recipients = RecipientsOf(users, new[] { recipientUserId });
            if (recipients.Count == 0) return;

            await notificationService.SendLocalizedToManyAsync(
                recipients,
                PushText.FromLiteral(tournamentName),
                PushText.FromKey("Push.OpponentReady.Body", opponentName),
                // teamMatchId rides along so a sub-match deep link opens the tie, not an empty solo
                // modal — same contract as the round-deadline and check-in pushes.
                new { tournamentId, matchId, teamMatchId, type = "opponentReady" });
        }

        private async Task MarkRegistrationRemindedAsync(Guid tournamentId, DateTime now, CancellationToken ct)
        {
            await context.Set<TournamentEntity>()
                .Where(t => t.Id == tournamentId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RegistrationDeadlineReminderSentOn, now), ct);
        }
    }
}
