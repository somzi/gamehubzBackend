using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.Logic.Services;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Api.BackgroundTasks
{
    /// <summary>
    /// Deletes inbox notifications older than the retention window (60 days by default). The inbox is a
    /// record of what happened lately, not an archive: by then every match a notification pointed at is
    /// long played and every request long answered, and the rows are pure cost.
    ///
    /// Permanent delete, for the same reason as <see cref="MatchChatRetentionRunner"/> — the row IS the
    /// cost, so a soft delete would save nothing. Age is the only rule, read or unread alike, and deleting
    /// the rows is its own marker: after the first drain an hourly pass finds only the last hour's expiry.
    /// </summary>
    public class NotificationRetentionRunner
    {
        private readonly ApplicationContext context;
        private readonly IConfiguration configuration;
        private readonly ILogger<NotificationRetentionRunner> logger;
        private readonly NotificationInboxService inboxService;

        public NotificationRetentionRunner(
            ApplicationContext context,
            IConfiguration configuration,
            ILogger<NotificationRetentionRunner> logger,
            NotificationInboxService inboxService)
        {
            this.context = context;
            this.configuration = configuration;
            this.logger = logger;
            this.inboxService = inboxService;
        }

        /// <summary>How long a notification stays in the inbox. The inbox page reports the same number to the app.</summary>
        private int RetentionDays =>
            GameHubz.DataModels.Consts.NotificationRetentionRules.ResolveRetentionDays(
                configuration.GetValue<int?>(GameHubz.DataModels.Consts.NotificationRetentionRules.RetentionDaysConfigKey));

        /// <summary>Rows deleted per statement, so a backlog never becomes one enormous transaction.</summary>
        private int SweepBatchSize =>
            Math.Clamp(configuration.GetValue("Notifications:SweepBatchSize", 5000), 100, 50000);

        /// <summary>Batches a single tick will run before going back to sleep.</summary>
        private int MaxBatchesPerSweep =>
            Math.Clamp(configuration.GetValue("Notifications:MaxBatchesPerSweep", 20), 1, 500);

        /// <summary>One pass of the retention rule. Returns the number of notifications deleted.</summary>
        public async Task<int> RunRetentionSweepAsync(CancellationToken cancellationToken = default)
        {
            DateTime cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            int total = 0;

            // Users who lost an UNREAD row: only those rows count toward the bell and the tabs.
            var usersWithChangedCounters = new HashSet<Guid>();

            for (int batch = 0; batch < MaxBatchesPerSweep; batch++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // Ids first, then the delete — one bounded statement per batch (IX_Notification_CreatedOn).
                var rows = await context.Set<NotificationEntity>()
                    .IgnoreQueryFilters()
                    .Where(n => n.CreatedOn < cutoff)
                    .Select(n => new { Id = n.Id!.Value, n.UserId, Unread = n.ReadOn == null })
                    .Take(SweepBatchSize)
                    .ToListAsync(cancellationToken);

                if (rows.Count == 0) break;

                List<Guid> ids = rows.Select(r => r.Id).ToList();

                total += await context.Set<NotificationEntity>()
                    .IgnoreQueryFilters()
                    .Where(n => ids.Contains(n.Id!.Value))
                    .ExecuteDeleteAsync(cancellationToken);

                foreach (var row in rows)
                {
                    if (row.Unread) usersWithChangedCounters.Add(row.UserId);
                }

                if (rows.Count < SweepBatchSize) break;
            }

            // Deleting unread rows lowered those users' counters. A device connected right now would keep
            // showing the old numbers until its next refetch; push the new ones like any other inbox write.
            // Best-effort — PushSummariesAsync never throws.
            if (usersWithChangedCounters.Count > 0)
                await inboxService.PushSummariesAsync(usersWithChangedCounters);

            if (total > 0)
            {
                logger.LogInformation(
                    "Notification retention sweep deleted {Count} notifications older than {Days} days.",
                    total, RetentionDays);
            }

            return total;
        }
    }
}
