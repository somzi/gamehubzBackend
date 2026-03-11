using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class UserHubRepository : BaseRepository<ApplicationContext, UserHubEntity>, IUserHubRepository
    {
        public UserHubRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public Task<UserHubEntity> GetByUserAndHub(Guid userId, Guid hubId)
        {
            return this.BaseDbSet()
                .FirstAsync(uh => uh.UserId == userId && uh.HubId == hubId)!;
        }

        public async Task<List<UserHubOverview>> GetUsersByHub(Guid hubId)
        {
            return await this.BaseDbSet()
                .Where(uh => uh.HubId == hubId && uh.HubId != null)
                .Select(x => new UserHubOverview
                {
                    UserId = x.UserId!.Value,
                    Username = x.User!.Username
                })
                .ToListAsync();
        }
    }
}