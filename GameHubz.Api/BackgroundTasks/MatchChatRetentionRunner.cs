using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Api.BackgroundTasks
{
    /// <summary>
    /// Deletes match chat once the tournament it belonged to is settled. A thread is a scheduling
    /// tool — "20:00 ok?", "gg" — not a record anyone comes back to, and every finished tournament
    /// leaves its threads behind forever. This retires them.
    ///
    /// The delete is permanent, not a soft delete: the row IS the cost here, so flagging it
    /// IsDeleted would keep every byte and save nothing. That is the whole point of the sweep, and
    /// the reason it does not reuse <see cref="GameHubz.Logic.Services.EvidenceRetentionService"/>,
    /// whose rows are soft-deleted only because each one holds the storage handle for a file that
    /// must not be stranded.
    ///
    /// Two rules, mirroring the evidence sweep:
    ///   • the normal one, keyed off when the tournament ended — and, like the evidence sweep, the
    ///     message must have cleared the grace window too, not just the tournament. A thread stays
    ///     writable until its match is Completed, and a cancelled tournament gets an EndedOn while
    ///     its matches are still open: testing only the tournament date deleted anything written
    ///     after that within the hour;
    ///   • an abandonment backstop, because a tournament that is never finished never satisfies
    ///     the first rule and would keep its threads forever.
    ///
    /// The backstop needs BOTH an old tournament and a dormant thread. Tournament age alone took a
    /// league that simply runs longer than six months and deleted its live threads mid-season. It
    /// still takes the whole thread at once — never a per-message cut, which would split a live
    /// thread in half (January gone while March is being written) — but only once nobody has
    /// written in it for a while.
    ///
    /// No marker column is needed to keep the work bounded, unlike the deadline sweeps: deleting
    /// the rows IS the marker. A match is only selected while it holds a message the rules would
    /// delete, so it never comes back empty-handed, and after the first drain the table only holds
    /// chat from tournaments that are live or ended within the grace window. A message posted on a
    /// settled match after its purge is collected once it has aged past the grace window itself.
    /// </summary>
    public class MatchChatRetentionRunner
    {
        private readonly ApplicationContext context;
        private readonly IConfiguration configuration;
        private readonly ILogger<MatchChatRetentionRunner> logger;

        public MatchChatRetentionRunner(
            ApplicationContext context,
            IConfiguration configuration,
            ILogger<MatchChatRetentionRunner> logger)
        {
            this.context = context;
            this.configuration = configuration;
            this.logger = logger;
        }

        /// <summary>Grace after a tournament ends before its threads are dropped.</summary>
        private int GraceDaysAfterTournamentEnd =>
            Math.Max(0, configuration.GetValue("MatchChat:RetentionDaysAfterTournamentEnd", 3));

        /// <summary>
        /// How old a tournament that never ended has to be before its chat is collected anyway.
        /// Deliberately far longer than the evidence backstop's 30 days: a clip costs real storage
        /// every day it is kept, a chat row costs almost nothing, so there is no reason to risk
        /// eating a genuinely long-running league's live thread to reclaim it sooner.
        /// </summary>
        private int AbandonedTournamentMaxAgeDays =>
            Math.Max(1, configuration.GetValue("MatchChat:AbandonedTournamentMaxAgeDays", 180));

        /// <summary>
        /// How long a thread in an abandoned-looking tournament must have gone without a message
        /// before the backstop takes it. Never shorter than the grace window, so the backstop's
        /// threads are always fully covered by the per-message cut the delete applies.
        /// </summary>
        private int AbandonedThreadInactiveDays =>
            Math.Max(GraceDaysAfterTournamentEnd, configuration.GetValue("MatchChat:AbandonedThreadInactiveDays", 30));

        /// <summary>Matches handled per batch. Each batch is its own statement, so a backlog never
        /// becomes one enormous transaction.</summary>
        private int SweepBatchSize =>
            Math.Clamp(configuration.GetValue("MatchChat:SweepBatchSize", 200), 1, 2000);

        /// <summary>
        /// Batches a single tick will run before going back to sleep. Exists for the first sweep
        /// after deploy, which has every tournament ever finished to work through: at one batch a
        /// tick that backlog would take weeks of hourly wake-ups to clear.
        /// </summary>
        private int MaxBatchesPerSweep =>
            Math.Clamp(configuration.GetValue("MatchChat:MaxBatchesPerSweep", 20), 1, 500);

        /// <summary>
        /// One pass of the retention rules. Returns the number of messages deleted.
        /// </summary>
        public async Task<int> RunRetentionSweepAsync(CancellationToken cancellationToken = default)
        {
            DateTime now = DateTime.UtcNow;
            DateTime endedBefore = now.AddDays(-GraceDaysAfterTournamentEnd);
            DateTime abandonedBefore = now.AddDays(-AbandonedTournamentMaxAgeDays);
            DateTime inactiveBefore = now.AddDays(-AbandonedThreadInactiveDays);

            int totalMessages = 0;
            int totalCursors = 0;
            int totalMatches = 0;

            for (int batch = 0; batch < MaxBatchesPerSweep; batch++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // IgnoreQueryFilters on every read and write here. MatchChat, MatchChatRead, Match
                // and Tournament all carry an IsDeleted filter, and a thread whose tournament or
                // match row was soft-deleted is precisely the thread that should go — left to the
                // filter it would be invisible to the sweep and kept forever.
                List<Guid> matchIds = await context.Set<MatchChatEntity>()
                    .IgnoreQueryFilters()
                    .Where(c => c.MatchId != null && c.Match!.Tournament != null)
                    .Where(c => c.CreatedOn < endedBefore)
                    .Where(c =>
                        // Normal path: the tournament is over and the grace window has passed —
                        // for the tournament and (above) for the message itself.
                        (c.Match!.Tournament!.EndedOn != null && c.Match.Tournament.EndedOn < endedBefore)
                        // Backstop: nothing ever ended, the tournament is too old to be live, and
                        // the thread has gone quiet. A long league still being played keeps its chat.
                        || (c.Match!.Tournament!.EndedOn == null
                            && c.Match.Tournament.CreatedOn < abandonedBefore
                            && !context.Set<MatchChatEntity>().Any(o => o.MatchId == c.MatchId && o.CreatedOn >= inactiveBefore)))
                    .Select(c => c.MatchId!.Value)
                    .Distinct()
                    .Take(SweepBatchSize)
                    .ToListAsync(cancellationToken);

                if (matchIds.Count == 0) break;

                // Same per-message cut as the selection. It is all a backstop thread holds anyway
                // (dormant means older than inactiveBefore, which is never later than endedBefore);
                // on the normal path it spares whatever was written inside the grace window.
                int messages = await context.Set<MatchChatEntity>()
                    .IgnoreQueryFilters()
                    .Where(c => c.MatchId != null && matchIds.Contains(c.MatchId.Value) && c.CreatedOn < endedBefore)
                    .ExecuteDeleteAsync(cancellationToken);

                // The read cursors go with the messages they pointed at — but only once the thread
                // is actually empty. Once it is gone a "last read" stamp and a per-thread mute
                // describe nothing, and there is one such row per participant per match. A thread
                // that kept a recent message keeps its cursors, or that message would read as unread
                // and a muted thread would start pushing again.
                int cursors = await context.Set<MatchChatReadEntity>()
                    .IgnoreQueryFilters()
                    .Where(r => matchIds.Contains(r.MatchId)
                        && !context.Set<MatchChatEntity>().IgnoreQueryFilters().Any(c => c.MatchId == r.MatchId))
                    .ExecuteDeleteAsync(cancellationToken);

                totalMessages += messages;
                totalCursors += cursors;
                totalMatches += matchIds.Count;
            }

            if (totalMatches > 0)
            {
                logger.LogInformation(
                    "Match chat retention sweep deleted {Messages} messages and {Cursors} read cursors across {Matches} matches.",
                    totalMessages, totalCursors, totalMatches);
            }

            return totalMessages;
        }
    }
}
