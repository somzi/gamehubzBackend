using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class UserStreamAccountRepository : BaseRepository<ApplicationContext, UserStreamAccountEntity>, IUserStreamAccountRepository
    {
        public UserStreamAccountRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<List<UserStreamAccountEntity>> GetByUserId(Guid userId)
        {
            return await this.BaseDbSet()
                .Where(x => x.UserId == userId)
                .OrderBy(x => x.Platform)
                .ToListAsync();
        }

        public async Task<UserStreamAccountEntity?> GetByUserAndPlatform(Guid userId, SocialType platform)
        {
            return await this.BaseDbSet()
                .Where(x => x.UserId == userId && x.Platform == platform)
                .FirstOrDefaultAsync();
        }

        public async Task<UserStreamAccountEntity?> GetByExternalUserId(SocialType platform, string externalUserId)
        {
            return await this.BaseDbSet()
                .Where(x => x.Platform == platform && x.ExternalUserId == externalUserId)
                .FirstOrDefaultAsync();
        }
    }
}
