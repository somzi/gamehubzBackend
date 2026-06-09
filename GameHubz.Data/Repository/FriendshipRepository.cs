using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class FriendshipRepository : BaseRepository<ApplicationContext, FriendshipEntity>, IFriendshipRepository
    {
        public FriendshipRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public Task<bool> AreFriends(Guid userAId, Guid userBId)
        {
            var (a, b) = SocialPair.Normalize(userAId, userBId);
            return this.BaseDbSet().AnyAsync(x => x.UserAId == a && x.UserBId == b);
        }

        public Task<FriendshipEntity?> Find(Guid userAId, Guid userBId)
        {
            var (a, b) = SocialPair.Normalize(userAId, userBId);
            return this.BaseDbSet().FirstOrDefaultAsync(x => x.UserAId == a && x.UserBId == b);
        }

        public Task<FriendshipEntity?> FindIncludingDeleted(Guid userAId, Guid userBId)
        {
            var (a, b) = SocialPair.Normalize(userAId, userBId);
            return this.BaseDbSet()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.UserAId == a && x.UserBId == b);
        }

        public Task<List<Guid>> GetFriendIds(Guid userId)
        {
            // Project just the OTHER-side userId for every active friendship row touching
            // this user. Small, no joins, perfect to populate the cached friends set.
            return this.BaseDbSet()
                .Where(x => x.UserAId == userId || x.UserBId == userId)
                .Select(x => x.UserAId == userId ? x.UserBId : x.UserAId)
                .ToListAsync();
        }

        public async Task<List<FriendDto>> GetFriendsOf(Guid userId, string? search)
        {
            // EF Core can't translate a conditional that picks one of two
            // navigation entities (`x.UserAId == userId ? x.UserB : x.UserA`),
            // so we project individual scalar fields with `?:` instead.
            var query = this.BaseDbSet()
                .Where(x => x.UserAId == userId || x.UserBId == userId)
                .Select(x => new FriendDto
                {
                    UserId = x.UserAId == userId ? x.UserBId : x.UserAId,
                    Username = x.UserAId == userId ? x.UserB!.Username : x.UserA!.Username,
                    Nickname = x.UserAId == userId ? x.UserB!.Nickname : x.UserA!.Nickname,
                    AvatarUrl = x.UserAId == userId ? x.UserB!.AvatarUrl : x.UserA!.AvatarUrl,
                    FriendsSince = x.CreatedOn!.Value
                });

            if (!string.IsNullOrWhiteSpace(search))
            {
                string s = search.ToLower();
                query = query.Where(f =>
                    f.Username.ToLower().Contains(s) ||
                    (f.Nickname != null && f.Nickname.ToLower().Contains(s)));
            }

            return await query
                .OrderBy(f => f.Username)
                .ToListAsync();
        }

    }
}
