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
    public class UserHubRequestRepository : BaseRepository<ApplicationContext, UserHubRequestEntity>, IUserHubRequestRepository
    {
        public UserHubRequestRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<List<UserHubRequestDto>> GetPendingRequestsByHubId(Guid hubId)
        {
            return await this.BaseDbSet()
                .Where(r => r.HubId == hubId && r.Status == JoinRequestStatus.Pending)
                .OrderByDescending(r => r.CreatedOn)
                .Select(r => new UserHubRequestDto
                {
                    RequestId = r.Id!.Value,
                    UserId = r.UserId!.Value,
                    HubId = r.HubId!.Value,
                    Username = r.User!.Username,
                    AvatarUrl = r.User.AvatarUrl,
                    RequestedAt = r.CreatedOn!.Value
                })
                .ToListAsync();
        }

        public async Task<UserHubRequestEntity?> GetPendingByHubAndUser(Guid hubId, Guid userId)
        {
            return await this.BaseDbSet()
                .FirstOrDefaultAsync(r => r.HubId == hubId && r.UserId == userId && r.Status == JoinRequestStatus.Pending);
        }

        public async Task<UserHubRequestEntity?> GetByIdWithHub(Guid requestId)
        {
            return await this.BaseDbSet()
                .Where(r => r.Id == requestId)
                .Include(r => r.Hub)
                .Include(r => r.User)
                .FirstOrDefaultAsync();
        }

        public async Task<bool> HasPendingRequest(Guid hubId, Guid userId)
        {
            return await this.BaseDbSet()
                .AnyAsync(r => r.HubId == hubId && r.UserId == userId && r.Status == JoinRequestStatus.Pending);
        }

        public async Task<int> CountPendingByHubId(Guid hubId)
        {
            return await this.BaseDbSet()
                .CountAsync(r => r.HubId == hubId && r.Status == JoinRequestStatus.Pending);
        }

        // Pending join requests across every hub the user manages — organizer badge.
        public async Task<int> CountPendingByHubIds(List<Guid> hubIds)
        {
            if (hubIds == null || hubIds.Count == 0) return 0;

            return await this.BaseDbSet()
                .CountAsync(r => r.HubId != null
                    && hubIds.Contains(r.HubId.Value)
                    && r.Status == JoinRequestStatus.Pending);
        }

        // Per-hub pending join-request counts — feeds the cascade badge on each hub card.
        public async Task<List<HubCountRow>> GetPendingCountsByHub(List<Guid> hubIds)
        {
            if (hubIds == null || hubIds.Count == 0) return new List<HubCountRow>();

            return await this.BaseDbSet()
                .Where(r => r.Status == JoinRequestStatus.Pending
                    && r.HubId != null
                    && hubIds.Contains(r.HubId.Value))
                .GroupBy(r => r.HubId!.Value)
                .Select(g => new HubCountRow { HubId = g.Key, Count = g.Count() })
                .ToListAsync();
        }
    }
}
