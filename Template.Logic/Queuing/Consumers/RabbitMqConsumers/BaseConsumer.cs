using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

using Template.DataModels.Config.RabbitMqConfig;

namespace Template.Logic.Queuing.Consumers.RabbitMqConsumers
{
    public abstract class BaseConsumer<TModel> : IBaseConsumer
    {
        protected ILocalizationService LocalizationService { get; init; }

        private readonly IRabbitMqConnectionService rabbitMqConnectionService;

        private readonly IRabbitMqConfigService rabbitMqConfigService;

        private readonly string consumerQueueName;

        private readonly QueueConfig queueConfig;

        private readonly IModel channel;

        public BaseConsumer(IServiceProvider serviceProvider, QueueConfig queueConfig, Queue queue)
        {
            this.rabbitMqConnectionService = serviceProvider.GetRequiredService<IRabbitMqConnectionService>();
            this.rabbitMqConfigService = serviceProvider.GetRequiredService<IRabbitMqConfigService>();
            this.LocalizationService = serviceProvider.GetRequiredService<ILocalizationService>();

            this.queueConfig = queueConfig;

            this.rabbitMqConfigService.ConfigureQueueIfNotExist(queueConfig);

            this.channel = this.rabbitMqConnectionService.GetServerConnection().CreateModel();

            this.consumerQueueName = queue.QueueName;
        }

        public void Subscribe()
        {
            if (queueConfig.UseSingleMessageRecieve)
            {
                this.channel.BasicQos(0, 1, false);
            }

            AsyncEventingBasicConsumer eventingBasicConsumer = new(this.channel);

            eventingBasicConsumer.Received += this.ReceivedMessage;

            this.channel.BasicConsume(consumerQueueName, false, eventingBasicConsumer);
        }

        protected abstract Task OnReceivedMessage(TModel model);

        protected void DisacknowledgeMessage(BasicDeliverEventArgs e)
        {
            this.channel.BasicNack(e.DeliveryTag, false, false);
        }

        protected void AcknowledgeMessage(BasicDeliverEventArgs e)
        {
            this.channel.BasicAck(e.DeliveryTag, false);
        }

        private async Task ReceivedMessage(object sender, BasicDeliverEventArgs e)
        {
            TModel? model = DeserializeMessageFromQueue<TModel>(e.Body.ToArray());

            if (model == null)
            {
                throw new InvalidDeserializedMessageException(this.LocalizationService);
            }

            try
            {
                await this.OnReceivedMessage(model);
                AcknowledgeMessage(e);
            }
            catch (Exception)
            {
                DisacknowledgeMessage(e);
            }
        }

        private static T? DeserializeMessageFromQueue<T>(byte[] bytes)
        {
            string jsonString = Encoding.UTF8.GetString(bytes);

            return JsonConvert.DeserializeObject<T>(jsonString);
        }
    }
}