using GameHubz.DataModels.Config.RabbitMqConfig;

namespace GameHubz.Logic.Interfaces
{
    public interface IRabbitMqQueueService
    {
        void Enqueue<TModel>(QueueConfig queueConfig, TModel message);
    }
}
