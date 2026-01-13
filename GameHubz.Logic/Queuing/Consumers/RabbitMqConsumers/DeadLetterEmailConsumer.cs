using GameHubz.DataModels.Config.RabbitMqConfig;

namespace GameHubz.Logic.Queuing.Consumers.RabbitMqConsumers
{
    public class DeadLetterEmailConsumer : BaseConsumer<EmailModel>
    {
        public DeadLetterEmailConsumer(IServiceProvider serviceProvider, QueueConfig queueConfig, Queue queue)
            : base(serviceProvider, queueConfig, queue)
        {
        }

        protected override async Task OnReceivedMessage(EmailModel model)
        {
            await Task.CompletedTask;
        }
    }
}
