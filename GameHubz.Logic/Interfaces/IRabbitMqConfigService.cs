using GameHubz.DataModels.Config.RabbitMqConfig;

namespace GameHubz.Logic.Interfaces
{
    public interface IRabbitMqConfigService
    {
        void ConfigureQueueIfNotExist(QueueConfig queueConfig);
    }
}
