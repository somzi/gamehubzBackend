using GameHubz.Logic.Queuing.Consumers.LocalQueueConsumers;

namespace GameHubz.Api.BackgroundTasks
{
    public class SendEmailTask(
        IServiceProvider serviceProvider,
        ILogger<SendEmailTask> logger,
        IConfiguration configuration) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (configuration.GetValueOrDefaultValue<bool>("BackgroundTasks:SendEmailTask:IsEnabled", false) == false)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                using (IServiceScope scope = serviceProvider.CreateScope())
                {
                    try
                    {
                        LocalQueueEmailConsumer? emailQueueConsumer = scope.ServiceProvider.GetService<LocalQueueEmailConsumer>();

                        if (emailQueueConsumer != null)
                        {
                            await emailQueueConsumer.SendNext();
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error in EmailQueueService");
                    }
                }

                await Task.Delay(1000 * 60, stoppingToken);
            }
        }
    }
}
