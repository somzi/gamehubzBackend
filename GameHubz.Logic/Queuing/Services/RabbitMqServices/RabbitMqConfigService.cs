using RabbitMQ.Client;
using GameHubz.DataModels.Config.RabbitMqConfig;

namespace GameHubz.Logic.Queuing.Services.RabbitMqServices
{
    public class RabbitMqConfigService : IRabbitMqConfigService
    {
        private readonly IRabbitMqConnectionService rabbitMqConnectionService;
        private readonly ILocalizationService localizationService;

        public RabbitMqConfigService(
            IRabbitMqConnectionService rabbitMqConnectionService,
            ILocalizationService localizationService)
        {
            this.rabbitMqConnectionService = rabbitMqConnectionService;
            this.localizationService = localizationService;
        }

        public void ConfigureQueueIfNotExist(QueueConfig queueConfig)
        {
            var serverConnection = this.rabbitMqConnectionService.GetServerConnection();

            if (serverConnection == null)
            {
                throw new RabbitMqInvalidServerConnectionException(this.localizationService);
            }

            Queue mainQueue = queueConfig.MainQueue;
            Queue deadLetterQueue = queueConfig.DeadLetterQueue;

            using IModel channel = serverConnection.CreateModel();

            ConfigQueue(channel, deadLetterQueue, null);
            ConfigQueue(channel, mainQueue, new Dictionary<string, object>
                {
                    { "x-dead-letter-exchange", deadLetterQueue.ExchangeName },
                    { "x-dead-letter-routing-key", deadLetterQueue.RoutingKeyName}
                });
        }

        private static void ConfigQueue(IModel channel, Queue queue, Dictionary<string, object>? queueDeclareArgs)
        {
            channel.ExchangeDeclare(
                queue.ExchangeName,
                ExchangeType.Direct,
                durable: true,
                autoDelete: false);

            channel.QueueDeclare(
                queue.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                queueDeclareArgs);

            channel.QueueBind(queue.QueueName, queue.ExchangeName, queue.RoutingKeyName, null);
        }
    }
}
