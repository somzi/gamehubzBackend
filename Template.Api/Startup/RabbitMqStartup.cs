using System.Data;
using Microsoft.Extensions.Options;
using Template.DataModels.Config.RabbitMqConfig;
using Template.Logic.Interfaces;
using Template.Logic.Queuing.Consumers.RabbitMqConsumers;

namespace Template.Api.Startup
{
    internal class RabbitMqStartup
    {
        internal static void ConfigureRabbitMqConsumers(
            IServiceProvider serviceProvider)
        {
            RabbitMq rabbitMq = serviceProvider.GetService<IOptions<RabbitMq>>()!.Value;

            if (rabbitMq.IsRabbitMqEnabled == false)
            {
                return;
            }

            ConsumerRegister? consumerRegister = serviceProvider.GetService<ConsumerRegister>();

            if (consumerRegister == null)
            {
                throw new NoNullAllowedException("Unable to resolve ConsumerRegister.");
            }

            foreach (QueueConfig queueConfig in rabbitMq.QueueConfigs)
            {
                InitConsumerBasedOnConfig(serviceProvider, queueConfig, consumerRegister);
            }
        }

        private static void InitConsumerBasedOnConfig(
            IServiceProvider serviceProvider,
            QueueConfig queueConfig,
            ConsumerRegister consumerRegister)
        {
            if (queueConfig.NumberOfConsumers == 0)
            {
                return;
            }

            Type? queueConsumer = Type.GetType(queueConfig.MainQueue.InitType);
            Type? deadLetterQueueConsumer = Type.GetType(queueConfig.DeadLetterQueue.InitType);

            if (queueConsumer == null || deadLetterQueueConsumer == null)
            {
                throw new NoNullAllowedException();
            }

            for (int i = 0; i < queueConfig.NumberOfConsumers; i++)
            {
                InitConsumer(
                    queueConsumer,
                    serviceProvider,
                    queueConfig,
                    queueConfig.MainQueue,
                    consumerRegister);
            }

            InitConsumer(
                deadLetterQueueConsumer,
                serviceProvider,
                queueConfig,
                queueConfig.DeadLetterQueue,
                consumerRegister);
        }

        private static void InitConsumer(
            Type queueConsumer,
            IServiceProvider serviceProvider,
            QueueConfig queueConfig,
            Queue queue,
            ConsumerRegister consumerRegister)
        {
            if (Activator.CreateInstance(
                        queueConsumer,
                        serviceProvider,
                        queueConfig,
                        queue) is not IBaseConsumer baseConsumer)
            {
                throw new NoNullAllowedException("Unable to activate Consumer.");
            }

            consumerRegister.RegisterConsumer(baseConsumer);

            baseConsumer.Subscribe();
        }
    }
}