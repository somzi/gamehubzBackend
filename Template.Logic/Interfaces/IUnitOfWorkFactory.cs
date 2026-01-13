namespace Template.Logic.Interfaces
{
    public interface IUnitOfWorkFactory
    {
        IAppUnitOfWork CreateAppUnitOfWork();
    }
}