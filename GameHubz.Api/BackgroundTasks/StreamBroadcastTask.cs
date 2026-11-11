namespace GameHubz.Api.BackgroundTasks
{
    /// <summary>
    /// Keeps in-app broadcasts honest when the phone can't: times out the ones whose heartbeats stopped,
    /// reminds the streamer when the match is over and finally tells the phone to stop. Each tick runs
    /// <see cref="StreamBroadcastRunner"/> on a fresh DI scope, like the deadline sweep.
    /// </summary>
    public class StreamBroadcastTask(
        IServiceProvider serviceProvider,
        ILogger<StreamBroadcastTask> logger,
        IConfiguration configuration) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (configuration.GetValue("BackgroundTasks:StreamBroadcastTask:IsEnabled", true) == false)
            {
                logger.LogInformation("StreamBroadcastTask is disabled via configuration.");
                return;
            }

            int intervalSeconds = Math.Max(
                15,
                configuration.GetValue("BackgroundTasks:StreamBroadcastTask:IntervalSeconds", 30));
            var interval = TimeSpan.FromSeconds(intervalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using IServiceScope scope = serviceProvider.CreateScope();
                    var runner = scope.ServiceProvider.GetRequiredService<StreamBroadcastRunner>();
                    await runner.RunAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "StreamBroadcastTask sweep failed.");
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
