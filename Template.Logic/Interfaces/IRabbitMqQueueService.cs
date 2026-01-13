using Template.DataModels.Config.RabbitMqConfig;

namespace Template.Logic.Interfaces
{
    public interface IRabbitMqQueueService
    {
        void Enqueue<TModel>(QueueConfig queueConfig, TModel message);
    }
}