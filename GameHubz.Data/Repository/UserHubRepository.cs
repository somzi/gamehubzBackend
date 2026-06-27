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

        // Hubs where the user can access exclusive tournaments: Owner, Admin or Exclusive.
        // Hub owners always have an Owner UserHub row (created on hub creation / backfilled by
        // migration 39), so this covers owners too.
        public async Task<List<Guid>> GetHubIdsWithExclusiveAccess(Guid userId)
        {
            return await this.BaseDbSet()
                .Where(uh => uh.UserId == userId
                    && uh.HubId != null
                    && (uh.HubRole == HubRole.HubOwner
                        || uh.HubRole == HubRole.HubAdmin
                        || uh.HubRole == HubRole.HubExclusive))
                .Select(uh => uh.HubId!.Value)
                .ToListAsync();
        }

        // Hubs the user manages (Owner or Admin) — i.e. can approve join requests /
        // registrations and act on admin-help. Drives the organizer badges. Owners always
        // have an Owner UserHub row (see GetHubIdsWithExclusiveAccess note), so this covers them.
        public async Task<List<Guid>> GetManagedHubIds(Guid userId)
        {
            return await this.BaseDbSet()
                .Where(uh => uh.UserId == userId
                    && uh.HubId != null
                    && (uh.HubRole == HubRole.HubOwner || uh.HubRole == HubRole.HubAdmin))
                .Select(uh => uh.HubId!.Value)
                .ToListAsync();
        }

        public async Task<List<UserHubOverview>> GetUsersByHub(Guid hubId)
        {
            return await this.BaseDbSet()
                .Where(uh => uh.HubId == hubId && uh.HubId != null)
                // Rank by privilege (Owner > Admin > Exclusive > Member) rather than the raw enum
                // value, since HubExclusive == 4 would otherwise sort after HubMember == 3.
                .OrderBy(uh => uh.HubRole == HubRole.HubOwner ? 0
                    : uh.HubRole == HubRole.HubAdmin ? 1
                    : uh.HubRole == HubRole.HubExclusive ? 2 : 3)
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
                .OrderBy(uh => uh.HubRole == HubRole.HubOwner ? 0
                    : uh.HubRole == HubRole.HubAdmin ? 1
                    : uh.HubRole == HubRole.HubExclusive ? 2 : 3)
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