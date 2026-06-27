using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class DirectChatRepository : BaseRepository<ApplicationContext, DirectChatEntity>, IDirectChatRepository
    {
        public DirectChatRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public Task<DirectChatEntity?> Find(Guid userAId, Guid userBId)
        {
            var (a, b) = SocialPair.Normalize(userAId, userBId);
            return this.BaseDbSet().FirstOrDefaultAsync(x => x.UserAId == a && x.UserBId == b);
        }

        public Task<DirectChatEntity?> GetByIdForUser(Guid chatId, Guid userId)
        {
            return this.BaseDbSet()
                .FirstOrDefaultAsync(x => x.Id == chatId && (x.UserAId == userId || x.UserBId == userId));
        }

        public async Task<List<DirectChatDto>> GetChatsForUser(Guid userId, string? search)
        {
            // Project to DTO with scalar conditionals so EF Core can translate.
            var query = this.BaseDbSet()
                .Where(x => x.UserAId == userId || x.UserBId == userId)
                .Select(x => new DirectChatDto
                {
                    Id = x.Id!.Value,
                    OtherUserId = x.UserAId == userId ? x.UserBId : x.UserAId,
                    OtherUsername = x.UserAId == userId ? x.UserB!.Username : x.UserA!.Username,
                    OtherNickname = x.UserAId == userId ? x.UserB!.Nickname : x.UserA!.Nickname,
                    OtherAvatarUrl = x.UserAId == userId ? x.UserB!.AvatarUrl : x.UserA!.AvatarUrl,
                    LastMessage = x.LastMessage,
                    LastMessageAt = x.LastMessageAt,
                    LastMessageSenderId = x.LastMessageSenderId,
                    UnreadCount = x.Messages!.Count(m => !m.IsRead && m.SenderId != userId)
                });

            if (!string.IsNullOrWhiteSpace(search))
            {
                string s = search.ToLower();
                query = query.Where(c =>
                    c.OtherUsername.ToLower().Contains(s) ||
                    (c.OtherNickname != null && c.OtherNickname.ToLower().Contains(s)));
            }

            return await query
                .OrderByDescending(c => c.LastMessageAt)
                .ToListAsync();
        }
    }
}
