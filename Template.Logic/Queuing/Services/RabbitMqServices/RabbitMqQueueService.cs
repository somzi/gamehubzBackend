using System.Text;
using RabbitMQ.Client;
using Template.DataModels.Config.RabbitMqConfig;

namespace Template.Logic.Queuing.Services.RabbitMqServices
{
    public class RabbitMqQueueService : IRabbitMqQueueService
    {
        private readonly ILocalizationService localizationService;
        private readonly IRabbitMqConnectionService rabbitMqConnection;
        private readonly IRabbitMqConfigService rabbitMqConfigService;

        public RabbitMqQueueService(
            ILocalizationService localizationService,
            IRabbitMqConnectionService rabbitMqConnection,
            IRabbitMqConfigService rabbitMqConfigService)
        {
            this.localizationService = localizationService;
            this.rabbitMqConnection = rabbitMqConnection;
            this.rabbitMqConfigService = rabbitMqConfigService;
        }

        public void Enqueue<TModel>(QueueConfig queueConfig, TModel message)
        {
            if (queueConfig == null)
            {
                throw new EmptyQueueConfigException(this.localizationService);
            }

            var serverConnection = this.rabbitMqConnection.GetServerConnection();

            this.rabbitMqConfigService.ConfigureQueueIfNotExist(queueConfig);

            byte[] serializedMessage = SerializeMessageForQueue(message);

            using var channel = serverConnection.CreateModel();

            var properties = channel.CreateBasicProperties();
            properties.DeliveryMode = 2;

            channel.BasicPublish(
                queueConfig.MainQueue.ExchangeName,
                queueConfig.MainQueue.RoutingKeyName,
                properties,
                serializedMessage);
        }

        private static byte[] SerializeMessageForQueue<TModel>(TModel message)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(message);
            return Encoding.UTF8.GetBytes(json);
        }
    }
}