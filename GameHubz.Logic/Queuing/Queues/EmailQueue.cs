using Microsoft.Extensions.Options;
using GameHubz.DataModels.Api;
using GameHubz.DataModels.Config.RabbitMqConfig;
using GameHubz.Logic.Queuing.Services.LocalQueueServices;

namespace GameHubz.Logic.Queuing.Queues
{
    public class EmailQueue
    {
        private readonly IRabbitMqQueueService rabbitMqService;
        private readonly LocalQueueEmailService localQueueEmailService;
        private readonly RabbitMq rabbitMqConfig;

        public EmailQueue(
            IRabbitMqQueueService rabbitMqService,
            LocalQueueEmailService localQueueEmailService,
            IOptions<RabbitMq> rabbitMqOption)
        {
            this.rabbitMqConfig = rabbitMqOption.Value;
            this.rabbitMqService = rabbitMqService;
            this.localQueueEmailService = localQueueEmailService;
        }

        public async Task Enqueue(EmailQueueModel message)
        {
            if (this.rabbitMqConfig.IsRabbitMqEnabled)
            {
                QueueConfig queueConfig = this.rabbitMqConfig.FindConfigByName("Email");

                this.rabbitMqService.Enqueue(queueConfig, message);
            }
            else
            {
                await this.localQueueEmailService.QueueEmail(message);
            }
        }
    }
}
