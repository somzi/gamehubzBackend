using RabbitMQ.Client;

namespace Template.Logic.Interfaces
{
    public interface IRabbitMqConnectionService
    {
        IConnection GetServerConnection();
    }
}