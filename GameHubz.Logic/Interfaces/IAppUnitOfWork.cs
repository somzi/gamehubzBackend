namespace GameHubz.Logic.Interfaces
{
    public interface IAppUnitOfWork : IDisposable, Common.Interfaces.IUnitOfWork
    {
        IUserRepository UserRepository { get; }

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
