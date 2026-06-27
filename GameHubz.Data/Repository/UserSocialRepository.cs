using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class UserSocialRepository : BaseRepository<ApplicationContext, UserSocialEntity>, IUserSocialRepository
    {
        public UserSocialRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<List<UserSocialEntity>> GetByUserId(Guid userId)
        {
            return await this.BaseDbSet()
                .Where(x => x.UserId == userId)
                .ToListAsync();
        }
    }
}