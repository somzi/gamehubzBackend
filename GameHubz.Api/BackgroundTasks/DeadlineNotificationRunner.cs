using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Api.BackgroundTasks
{
    /// <summary>
    /// One pass of the deadline-reminder sweeps, run on its own DI scope by
    /// <see cref="DeadlineNotificationTask"/>. Reads straight off ApplicationContext (like
    /// ShareController) and marks each reminded row with ExecuteUpdate so a row is never
    /// re-evaluated on the next tick — that "sent" marker is what keeps the push one-shot.
    /// </summary>
    public class DeadlineNotificationRunner
    {
        // The last-call lead is capped at RoundFinalLeadTimeMinutes but shrinks to this fraction
        // of the round length for short rounds, so it always lands after the round opens (a fixed
        // 3h last-call would never fire on a 1-hour round — its players would get nothing).
        private const double RoundFinalLeadFraction = 0.4;

        private readonly ApplicationContext context;
        private readonly INotificationService notificationService;
        private readonly ILogger<DeadlineNotificationRunner> logger;
        private readonly int registrationLeadMinutes;
        private readonly int roundEarlyLeadMinutes;
        private readonly int roundFinalLeadMinutes;

        public DeadlineNotificationRunner(
            ApplicationContext context,
            INotificationService notificationService,
            IConfiguration configuration,
            ILogger<DeadlineNotificationRunner> logger)
        {
            this.context = context;
            this.notificationService = notificationService;
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
        }

        public async Task RunAsync(CancellationToken ct)
        {
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
                        var tokens = await context.Set<UserEntity>()
                            .AsNoTracking()
                            .Where(u => targetIds.Contains(u.Id!.Value) && u.IsActive && u.PushToken != null)
                            .Select(u => u.PushToken!)
                            .ToListAsync(ct);

                        if (tokens.Count > 0)
                        {
                            await notificationService.SendToManyAsync(
                                tokens,
                                tournament.Name,
                                "Registration closes soon — join before the deadline!",
                                new { tournamentId = tournament.Id, type = "registrationDeadline" });
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

            foreach (var match in due)
            {
                if (ct.IsCancellationRequested) return;

                try
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

                    int newStage;
                    string body;

                    if (match.RoundReminderStage < 1 && earlyEligible && now >= earlyAt && now < lastCallAt)
                    {
                        newStage = 1;
                        body = "Don't forget to play your match before the round deadline.";
                    }
                    else if (now >= lastCallAt)
                    {
                        // Covers both the normal last-call and the case where we missed the early
                        // window (task was down) — we jump straight to the final reminder, never both.
                        newStage = 2;
                        body = "Final call — play your match before time runs out!";
                    }
                    else
                    {
                        continue; // not inside any reminder window yet
                    }

                    var userIds = new List<Guid>();
                    if (match.HomeUserId.HasValue) userIds.Add(match.HomeUserId.Value);
                    if (match.AwayUserId.HasValue) userIds.Add(match.AwayUserId.Value);

                    if (userIds.Count > 0)
                    {
                        var tokens = await context.Set<UserEntity>()
                            .AsNoTracking()
                            .Where(u => userIds.Contains(u.Id!.Value) && u.IsActive && u.PushToken != null)
                            .Select(u => u.PushToken!)
                            .ToListAsync(ct);

                        if (tokens.Count > 0)
                        {
                            await notificationService.SendToManyAsync(
                                tokens,
                                match.TournamentName,
                                body,
                                // teamMatchId — carried for team-tournament sub-matches so the mobile deep
                                // link can route to the team-match modal (the solo modal renders empty for
                                // a sub-match id).
                                new { tournamentId = match.TournamentId, matchId = match.Id, teamMatchId = match.TeamMatchId, type = "roundDeadline" });
                        }
                    }

                    await context.Set<MatchEntity>()
                        .Where(m => m.Id == match.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.RoundReminderStage, newStage), ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed round reminder for match {MatchId}.", match.Id);
                }
            }
        }

        private async Task MarkRegistrationRemindedAsync(Guid tournamentId, DateTime now, CancellationToken ct)
        {
            await context.Set<TournamentEntity>()
                .Where(t => t.Id == tournamentId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RegistrationDeadlineReminderSentOn, now), ct);
        }
    }
}
