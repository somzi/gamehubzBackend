namespace Template.DataModels.Config.RabbitMqConfig
{
    public class QueueConfig
    {
        public string Name { get; set; } = string.Empty;

        public bool UseSingleMessageRecieve { get; set; }

        public Queue MainQueue { get; set; } = new Queue();

        public Queue DeadLetterQueue { get; set; } = new Queue();

        public int NumberOfConsumers { get; set; }
    }
}