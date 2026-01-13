using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;

namespace GameHubz.Data.Repository
{
    public class RefreshTokenRepository : BaseRepository<ApplicationContext, RefreshTokenEntity>, IRefreshTokenRepository
    {
        public RefreshTokenRepository(ApplicationContext context, DateTimeProvider dateTimeProvider, IFilterExpressionBuilder filterExpressionBuilder, ISortStringBuilder sortStringBuilder, ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public RefreshTokenEntity? FindByUserIdAndTokenValue(Guid userId, string token)
        {
            return this.BaseDbSet()
                .Where(x => x.UserId == userId && x.Token == token)
                .SingleOrDefault();
        }

        public RefreshTokenEntity? FindByTokenValue(string token)
        {
            return this.BaseDbSet()
                .Where(x => x.Token == token)
                .SingleOrDefault();
        }
    }
}
