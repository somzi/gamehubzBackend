using RabbitMQ.Client;

namespace GameHubz.Logic.Interfaces
{
    public interface IRabbitMqConnectionService
    {
        IConnection GetServerConnection();
    }
}
