using GameHubz.Logic.Services;

namespace GameHubz.Api.BackgroundTasks
{
    /// <summary>
    /// Periodically retires match evidence that has outlived its purpose — in practice, video,
    /// which is the only part of evidence whose storage and bandwidth we feel.
    ///
    /// Runs hourly by default: the retention windows are measured in days, so anything tighter
    /// just wakes a scope up to find nothing. Each tick takes a fresh DI scope for its own
    /// DbContext, mirroring the other sweeps here. Batched inside the service, so a backlog is
    /// worked off a slice per tick rather than in one long transaction.
    /// </summary>
    public class EvidenceRetentionTask(
        IServiceProvider serviceProvider,
        ILogger<EvidenceRetentionTask> logger,
        IConfiguration configuration) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (configuration.GetValue("BackgroundTasks:EvidenceRetentionTask:IsEnabled", true) == false)
            {
                logger.LogInformation("EvidenceRetentionTask is disabled via configuration.");
                return;
            }

            int intervalSeconds = Math.Max(
                60,
                configuration.GetValue("BackgroundTasks:EvidenceRetentionTask:IntervalSeconds", 3600));
            var interval = TimeSpan.FromSeconds(intervalSeconds);

            // Nothing here is time-critical — the windows are days wide — so the first sweep waits
            // rather than racing schema migrations at boot. Without it, a deploy that adds the
            // columns this reads would log a failed sweep before the migration lands.
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
                    var retention = scope.ServiceProvider.GetRequiredService<EvidenceRetentionService>();
                    await retention.RunRetentionSweepAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "EvidenceRetentionTask sweep failed.");
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
