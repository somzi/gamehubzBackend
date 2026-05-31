using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class UserBlockRepository : BaseRepository<ApplicationContext, UserBlockEntity>, IUserBlockRepository
    {
        public UserBlockRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public Task<bool> IsBlocked(Guid blockerId, Guid blockedId)
        {
            return this.BaseDbSet().AnyAsync(x => x.BlockerId == blockerId && x.BlockedId == blockedId);
        }

        public Task<bool> EitherBlocks(Guid userAId, Guid userBId)
        {
            return this.BaseDbSet().AnyAsync(x =>
                (x.BlockerId == userAId && x.BlockedId == userBId) ||
                (x.BlockerId == userBId && x.BlockedId == userAId));
        }

        public Task<UserBlockEntity?> Find(Guid blockerId, Guid blockedId)
        {
            return this.BaseDbSet().FirstOrDefaultAsync(x => x.BlockerId == blockerId && x.BlockedId == blockedId);
        }

        public Task<UserBlockEntity?> FindIncludingDeleted(Guid blockerId, Guid blockedId)
        {
            return this.BaseDbSet()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.BlockerId == blockerId && x.BlockedId == blockedId);
        }

        public async Task<List<BlockedUserDto>> GetBlockedList(Guid blockerId, string? search)
        {
            var query = this.BaseDbSet().Where(x => x.BlockerId == blockerId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                string s = search.ToLower();
                query = query.Where(x => x.Blocked!.Username.ToLower().Contains(s));
            }

            return await query
                .OrderByDescending(x => x.CreatedOn)
                .Select(x => new BlockedUserDto
                {
                    UserId = x.BlockedId,
                    Username = x.Blocked!.Username,
                    AvatarUrl = x.Blocked!.AvatarUrl,
                    BlockedAt = x.CreatedOn!.Value
                })
                .ToListAsync();
        }
    }
}
