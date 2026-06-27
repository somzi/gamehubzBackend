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
    public class FriendRequestRepository : BaseRepository<ApplicationContext, FriendRequestEntity>, IFriendRequestRepository
    {
        public FriendRequestRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public Task<FriendRequestEntity?> FindPendingBetween(Guid userAId, Guid userBId)
        {
            return this.BaseDbSet()
                .Where(x => x.Status == FriendRequestStatus.Pending)
                .FirstOrDefaultAsync(x =>
                    (x.FromUserId == userAId && x.ToUserId == userBId) ||
                    (x.FromUserId == userBId && x.ToUserId == userAId));
        }

        public async Task<List<FriendRequestDto>> GetIncomingPending(Guid userId, string? search)
        {
            var query = this.BaseDbSet()
                .Where(x => x.ToUserId == userId && x.Status == FriendRequestStatus.Pending);

            if (!string.IsNullOrWhiteSpace(search))
            {
                string s = search.ToLower();
                query = query.Where(x =>
                    x.FromUser!.Username.ToLower().Contains(s) ||
                    (x.FromUser!.Nickname != null && x.FromUser!.Nickname.ToLower().Contains(s)));
            }

            return await query
                .OrderByDescending(x => x.CreatedOn)
                .Select(x => new FriendRequestDto
                {
                    Id = x.Id!.Value,
                    FromUserId = x.FromUserId,
                    FromUsername = x.FromUser!.Username,
                    FromNickname = x.FromUser!.Nickname,
                    FromAvatarUrl = x.FromUser!.AvatarUrl,
                    ToUserId = x.ToUserId,
                    ToUsername = x.ToUser!.Username,
                    ToNickname = x.ToUser!.Nickname,
                    ToAvatarUrl = x.ToUser!.AvatarUrl,
                    Status = x.Status,
                    CreatedOn = x.CreatedOn!.Value
                })
                .ToListAsync();
        }

        public Task<int> GetIncomingPendingCount(Guid userId)
        {
            return this.BaseDbSet()
                .CountAsync(x => x.ToUserId == userId && x.Status == FriendRequestStatus.Pending);
        }

        public async Task<List<FriendRequestDto>> GetOutgoingPending(Guid userId, string? search)
        {
            var query = this.BaseDbSet()
                .Where(x => x.FromUserId == userId && x.Status == FriendRequestStatus.Pending);

            if (!string.IsNullOrWhiteSpace(search))
            {
                string s = search.ToLower();
                query = query.Where(x =>
                    x.ToUser!.Username.ToLower().Contains(s) ||
                    (x.ToUser!.Nickname != null && x.ToUser!.Nickname.ToLower().Contains(s)));
            }

            return await query
                .OrderByDescending(x => x.CreatedOn)
                .Select(x => new FriendRequestDto
                {
                    Id = x.Id!.Value,
                    FromUserId = x.FromUserId,
                    FromUsername = x.FromUser!.Username,
                    FromNickname = x.FromUser!.Nickname,
                    FromAvatarUrl = x.FromUser!.AvatarUrl,
                    ToUserId = x.ToUserId,
                    ToUsername = x.ToUser!.Username,
                    ToNickname = x.ToUser!.Nickname,
                    ToAvatarUrl = x.ToUser!.AvatarUrl,
                    Status = x.Status,
                    CreatedOn = x.CreatedOn!.Value
                })
                .ToListAsync();
        }
    }
}
