namespace Template.Logic.Queuing.Consumers.RabbitMqConsumers
{
    public class ConsumerRegister
    {
        private readonly List<IBaseConsumer> consumers = new();

        public void RegisterConsumer(IBaseConsumer consumer)
        {
            this.consumers.Add(consumer);
        }
    }
}