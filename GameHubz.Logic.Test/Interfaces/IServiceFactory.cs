namespace GameHubz.Logic.Test.Interfaces
{
    public interface IServiceFactory<TService>
    {
        TService CreateService();
    }
}
