using GameHubz.Data.Context;
using GameHubz.Data.Repository;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;

namespace GameHubz.Data.UnitOfWork
{
    public class AppUnitOfWork : BaseUnitOfWork, IAppUnitOfWork
    {
        public AppUnitOfWork(ApplicationContext context, DateTimeProvider dateTimeProvider, IFilterExpressionBuilder filterExpressionBuilder, ISortStringBuilder sortStringBuilder, ILocalizationService localizationService) : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public IUserRepository UserRepository => this.GetRepository<UserRepository>();

        public IUserRoleRepository UserRoleRepository => this.GetRepository<UserRoleRepository>();

        public IRefreshTokenRepository RefreshTokenRepository => this.GetRepository<RefreshTokenRepository>();

        public IAssetRepository AssetRepository => this.GetRepository<AssetRepository>();

        public IEmailQueueRepository EmailQueueRepository => this.GetRepository<EmailQueueRepository>();

        //***********************************************
        //********** GENERATED **************************
        //***********************************************

        // DO NOT DELETE - Generated Repository Tag
    }
}
