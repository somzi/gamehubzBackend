using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class UserHubBanRepository : BaseRepository<ApplicationContext, UserHubBanEntity>, IUserHubBanRepository
    {
        public UserHubBanRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public Task<bool> IsBanned(Guid userId, Guid hubId)
        {
            return this.BaseDbSet()
                .AnyAsync(b => b.UserId == userId && b.HubId == hubId);
        }

        public Task<UserHubBanEntity?> FindActiveBan(Guid userId, Guid hubId)
        {
            return this.BaseDbSet()
                .FirstOrDefaultAsync(b => b.UserId == userId && b.HubId == hubId);
        }
    }
}
