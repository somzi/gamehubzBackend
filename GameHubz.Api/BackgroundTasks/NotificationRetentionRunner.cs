using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
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

        public NotificationRetentionRunner(
            ApplicationContext context,
            IConfiguration configuration,
            ILogger<NotificationRetentionRunner> logger)
        {
            this.context = context;
            this.configuration = configuration;
            this.logger = logger;
        }

        /// <summary>How long a notification stays in the inbox. The app's end-of-list footer quotes the default.</summary>
        private int RetentionDays =>
            Math.Max(1, configuration.GetValue("Notifications:RetentionDays", 60));

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

            for (int batch = 0; batch < MaxBatchesPerSweep; batch++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // Ids first, then the delete — one bounded statement per batch (IX_Notification_CreatedOn).
                List<Guid> ids = await context.Set<NotificationEntity>()
                    .IgnoreQueryFilters()
                    .Where(n => n.CreatedOn < cutoff)
                    .Select(n => n.Id!.Value)
                    .Take(SweepBatchSize)
                    .ToListAsync(cancellationToken);

                if (ids.Count == 0) break;

                total += await context.Set<NotificationEntity>()
                    .IgnoreQueryFilters()
                    .Where(n => ids.Contains(n.Id!.Value))
                    .ExecuteDeleteAsync(cancellationToken);

                if (ids.Count < SweepBatchSize) break;
            }

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
