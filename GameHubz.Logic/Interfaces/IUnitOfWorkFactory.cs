namespace GameHubz.Logic.Interfaces
{
    public interface IUnitOfWorkFactory
    {
        IAppUnitOfWork CreateAppUnitOfWork();
    }
}
