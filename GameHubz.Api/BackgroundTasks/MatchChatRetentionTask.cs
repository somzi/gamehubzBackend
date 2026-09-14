namespace GameHubz.Api.BackgroundTasks
{
    /// <summary>
    /// Periodically deletes match chat whose tournament is settled — see
    /// <see cref="MatchChatRetentionRunner"/> for the rules.
    ///
    /// Runs hourly by default: the retention windows are measured in days, so anything tighter
    /// just wakes a scope up to find nothing. Each tick takes a fresh DI scope for its own
    /// DbContext, mirroring the other sweeps here. Batched inside the runner, so the backlog the
    /// first sweep after deploy inherits is worked off in slices rather than one long transaction.
    /// </summary>
    public class MatchChatRetentionTask(
        IServiceProvider serviceProvider,
        ILogger<MatchChatRetentionTask> logger,
        IConfiguration configuration) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (configuration.GetValue("BackgroundTasks:MatchChatRetentionTask:IsEnabled", true) == false)
            {
                logger.LogInformation("MatchChatRetentionTask is disabled via configuration.");
                return;
            }

            int intervalSeconds = Math.Max(
                60,
                configuration.GetValue("BackgroundTasks:MatchChatRetentionTask:IntervalSeconds", 3600));
            var interval = TimeSpan.FromSeconds(intervalSeconds);

            // Nothing here is time-critical — the windows are days wide — so the first sweep waits
            // rather than racing schema migrations at boot.
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
                    var retention = scope.ServiceProvider.GetRequiredService<MatchChatRetentionRunner>();
                    await retention.RunRetentionSweepAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "MatchChatRetentionTask sweep failed.");
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
