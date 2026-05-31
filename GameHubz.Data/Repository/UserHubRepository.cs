using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
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

        public Task<UserHubEntity?> FindByUserAndHub(Guid userId, Guid hubId)
        {
            return this.BaseDbSet()
                .FirstOrDefaultAsync(uh => uh.UserId == userId && uh.HubId == hubId);
        }

        public Task<HubRole?> GetRole(Guid userId, Guid hubId)
        {
            return this.BaseDbSet()
                .Where(uh => uh.UserId == userId && uh.HubId == hubId)
                .Select(uh => (HubRole?)uh.HubRole)
                .FirstOrDefaultAsync();
        }

        public async Task<List<UserHubOverview>> GetUsersByHub(Guid hubId)
        {
            return await this.BaseDbSet()
                .Where(uh => uh.HubId == hubId && uh.HubId != null)
                .OrderBy(uh => uh.HubRole)
                .ThenBy(uh => uh.User!.Username)
                .Select(x => new UserHubOverview
                {
                    UserId = x.UserId!.Value,
                    Username = x.User!.Username,
                    PushToken = x.User!.PushToken,
                    AvatarUrl = x.User!.AvatarUrl,
                    HubRole = x.HubRole
                })
                .ToListAsync();
        }

        public async Task<List<UserHubOverview>> GetUsersByHubPaged(Guid hubId, int pageNumber, int pageSize, string? search)
        {
            var query = this.BaseDbSet()
                .Where(uh => uh.HubId == hubId && uh.HubId != null);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(uh => uh.User != null && uh.User.Username.ToLower().Contains(term));
            }

            return await query
                .OrderBy(uh => uh.HubRole)
                .ThenBy(uh => uh.User!.Username)
                .Skip(pageNumber * pageSize)
                .Take(pageSize)
                .Select(x => new UserHubOverview
                {
                    UserId = x.UserId!.Value,
                    Username = x.User!.Username,
                    PushToken = x.User!.PushToken,
                    AvatarUrl = x.User!.AvatarUrl,
                    HubRole = x.HubRole
                })
                .ToListAsync();
        }
    }
}