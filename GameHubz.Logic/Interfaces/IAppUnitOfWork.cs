namespace GameHubz.Logic.Interfaces
{
    public interface IAppUnitOfWork : IDisposable, IUnitOfWork
    {
        IUserRepository UserRepository { get; }

        IHubRepository HubRepository { get; }

        IUserRoleRepository UserRoleRepository { get; }

        IRefreshTokenRepository RefreshTokenRepository { get; }

        IAssetRepository AssetRepository { get; }

        IEmailQueueRepository EmailQueueRepository { get; }

        //***********************************************
        //********** GENERATED **************************
        //***********************************************

        // DO NOT DELETE - Generated Repository Tag
    }
}