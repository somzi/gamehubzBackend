namespace GameHubz.Api.BackgroundTasks
{
    /// <summary>
    /// Periodically deletes inbox notifications past their retention window — see
    /// <see cref="NotificationRetentionRunner"/> for the rule.
    ///
    /// Hourly by default: the window is measured in days. A task of its own rather than a pass inside the
    /// one-minute deadline sweep, so it neither runs sixty times more often than it needs to nor shares an
    /// on/off switch with the reminders. Each tick takes a fresh DI scope for its own DbContext.
    /// </summary>
    public class NotificationRetentionTask(
        IServiceProvider serviceProvider,
        ILogger<NotificationRetentionTask> logger,
        IConfiguration configuration) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (configuration.GetValue("BackgroundTasks:NotificationRetentionTask:IsEnabled", true) == false)
            {
                logger.LogInformation("NotificationRetentionTask is disabled via configuration.");
                return;
            }

            int intervalSeconds = Math.Max(
                60,
                configuration.GetValue("BackgroundTasks:NotificationRetentionTask:IntervalSeconds", 3600));
            var interval = TimeSpan.FromSeconds(intervalSeconds);

            // Nothing here is time-critical, so the first sweep waits rather than racing schema
            // migrations at boot.
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using IServiceScope scope = serviceProvider.CreateScope();
                    var retention = scope.ServiceProvider.GetRequiredService<NotificationRetentionRunner>();
                    await retention.RunRetentionSweepAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "NotificationRetentionTask sweep failed.");
                }

                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}
