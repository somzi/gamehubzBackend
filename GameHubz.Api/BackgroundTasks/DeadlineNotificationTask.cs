namespace GameHubz.Api.BackgroundTasks
{
    /// <summary>
    /// Periodically nudges users about approaching deadlines:
    ///   • a tournament's registration is about to close (eligible hub members not yet registered)
    ///   • a match's round deadline is approaching (the players who have not played it yet)
    /// Each tick runs on a fresh DI scope so it gets its own DbContext, mirroring SendEmailTask.
    /// The per-row "reminder sent" markers make every push one-shot, so it is also restart-safe.
    /// </summary>
    public class DeadlineNotificationTask(
        IServiceProvider serviceProvider,
        ILogger<DeadlineNotificationTask> logger,
        IConfiguration configuration) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (configuration.GetValue("BackgroundTasks:DeadlineNotificationTask:IsEnabled", true) == false)
            {
                logger.LogInformation("DeadlineNotificationTask is disabled via configuration.");
                return;
            }

            int intervalSeconds = Math.Max(
                15,
                configuration.GetValue("BackgroundTasks:DeadlineNotificationTask:IntervalSeconds", 60));
            var interval = TimeSpan.FromSeconds(intervalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using IServiceScope scope = serviceProvider.CreateScope();
                    var runner = scope.ServiceProvider.GetRequiredService<DeadlineNotificationRunner>();
                    await runner.RunAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "DeadlineNotificationTask sweep failed.");
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
