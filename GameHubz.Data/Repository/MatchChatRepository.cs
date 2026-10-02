using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class MatchChatRepository : BaseRepository<ApplicationContext, MatchChatEntity>, IMatchChatRepository
    {
        public MatchChatRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<List<ChatMessageDto>> GetByMatchId(Guid matchId, int? take = null, DateTime? before = null)
        {
            var q = this.BaseDbSet().Where(x => x.MatchId == matchId);

            if (before.HasValue)
            {
                q = q.Where(x => x.CreatedOn < before.Value);
            }

            // With a take we grab the most recent N (descending) before projecting; without one
            // the full history is returned (legacy callers, e.g. MatchScheduleCard). Either way
            // the result is flipped back to oldest→newest for display.
            if (take.HasValue)
            {
                q = q.OrderByDescending(x => x.CreatedOn).Take(take.Value);
            }

            return await q
                .Select(x => new ChatMessageDto
                {
                    Id = x.Id!.Value,
                    UserId = x.UserId!.Value,
                    // Nickname can be persisted as "" (entity default) — treat blank as unset.
                    UserNickname = string.IsNullOrWhiteSpace(x.User!.Nickname) ? x.User!.Username : x.User!.Nickname!,
                    UserAvatarUrl = x.User!.AvatarUrl,
                    Content = x.Content,
                    SentAt = x.CreatedOn!.Value
                })
                .OrderBy(x => x.SentAt)
                .ToListAsync();
        }

        public async Task<Dictionary<Guid, int>> GetUnreadCountsByMatch(List<Guid> matchIds, Guid userId)
        {
            if (matchIds == null || matchIds.Count == 0)
            {
                return new Dictionary<Guid, int>();
            }

            // Join the user's live read cursors once instead of probing the cursor table
            // in scalar subqueries for every message. No cursor means all others' messages
            // are unread; otherwise only messages strictly after LastReadAt count.
            var cursors = this.ContextBase.Set<MatchChatReadEntity>()
                .AsNoTracking()
                .Where(r => r.UserId == userId);
            var unread =
                from message in this.BaseDbSet()
                join cursor in cursors on message.MatchId equals (Guid?)cursor.MatchId into readCursors
                from cursor in readCursors.DefaultIfEmpty()
                where message.MatchId != null
                    && matchIds.Contains(message.MatchId.Value)
                    && message.UserId != userId
                    && (cursor == null || message.CreatedOn > cursor.LastReadAt)
                select message;

            var rows = await unread
                .GroupBy(message => message.MatchId!.Value)
                .Select(g => new { MatchId = g.Key, Count = g.Count() })
                .ToListAsync();

            return rows.ToDictionary(x => x.MatchId, x => x.Count);
        }

        public async Task<List<Guid>> GetChatUserIds(Guid matchId)
        {
            return await this.BaseDbSet()
                .Where(x => x.MatchId == matchId && x.UserId != null)
                .Select(x => x.UserId!.Value)
                .Distinct()
                .ToListAsync();
        }

        public Task<int> DeleteByMatchIds(IReadOnlyCollection<Guid> matchIds)
        {
            if (matchIds.Count == 0) return Task.FromResult(0);

            // IgnoreQueryFilters: a soft-deleted message is still a row pointing at the match.
            return this.ContextBase.Set<MatchChatEntity>()
                .IgnoreQueryFilters()
                .Where(x => x.MatchId != null && matchIds.Contains(x.MatchId.Value))
                .ExecuteDeleteAsync();
        }
    }
}
