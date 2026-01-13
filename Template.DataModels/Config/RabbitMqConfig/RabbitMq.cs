namespace Template.DataModels.Config.RabbitMqConfig
{
    public class RabbitMq
    {
        public bool IsRabbitMqEnabled { get; set; }

        public List<QueueConfig> QueueConfigs { get; set; } = new();

        public QueueConfig FindConfigByName(string configName)
        {
            var queueConfig = this.QueueConfigs.SingleOrDefault(x => x.Name == configName);

            if (queueConfig == null)
            {
                throw new Exception($"Unable to find queue config: '{configName}'");
            }

            return queueConfig;
        }
    }
}