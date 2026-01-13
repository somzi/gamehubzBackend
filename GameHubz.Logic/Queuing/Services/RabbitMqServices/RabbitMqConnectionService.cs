using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace GameHubz.Logic.Queuing.Services.RabbitMqServices
{
    public class RabbitMqConnectionService : IRabbitMqConnectionService
    {
        private readonly Lazy<IConnection> serverConnectionLazy;
        private readonly ILocalizationService localizationService;
        private readonly string rabbitMqConnection;

        public RabbitMqConnectionService(
            IConfiguration configuration,
            ILocalizationService localizationService)
        {
            this.rabbitMqConnection = configuration.GetValueThrowIfNull<string>("ConnectionStrings:RabbitMqConnection");
            this.localizationService = localizationService;
            this.serverConnectionLazy = new Lazy<IConnection>(this.GetServerConnectionInit);
        }

        public IConnection GetServerConnection()
        {
            return this.serverConnectionLazy.Value;
        }

        private IConnection GetServerConnectionInit()
        {
            var serverConnection = this.ConnectToRabbitMqServer();

            if (serverConnection == null)
            {
                throw new RabbitMqInvalidServerConnectionException(this.localizationService);
            }

            return serverConnection;
        }

        private IConnection ConnectToRabbitMqServer()
        {
            var connectionFactory = new ConnectionFactory
            {
                Uri = new Uri(this.rabbitMqConnection),
                AutomaticRecoveryEnabled = true,
                DispatchConsumersAsync = true
            };

            return connectionFactory.CreateConnection();
        }
    }
}
