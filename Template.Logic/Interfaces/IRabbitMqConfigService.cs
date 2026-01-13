using Template.DataModels.Config.RabbitMqConfig;

namespace Template.Logic.Interfaces
{
    public interface IRabbitMqConfigService
    {
        void ConfigureQueueIfNotExist(QueueConfig queueConfig);
    }
}