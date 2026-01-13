namespace GameHubz.DataModels.Config.RabbitMqConfig
{
    public class Queue
    {
        public string QueueName { get; set; } = string.Empty;

        public string ExchangeName { get; set; } = string.Empty;

        public string RoutingKeyName { get; set; } = string.Empty;

        public string InitType { get; set; } = string.Empty;
    }
}
