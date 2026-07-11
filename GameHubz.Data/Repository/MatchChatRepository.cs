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

            // Correlated subquery resolves each user's per-match read cursor; a message
            // counts as unread when there is no cursor or it was sent after the cursor.
            var rows = await this.ContextBase.Set<MatchChatEntity>()
                .AsNoTracking()
                .Where(mc => mc.MatchId != null
                    && matchIds.Contains(mc.MatchId.Value)
                    && mc.UserId != userId)
                .Select(mc => new
                {
                    MatchId = mc.MatchId!.Value,
                    mc.CreatedOn,
                    LastRead = this.ContextBase.Set<MatchChatReadEntity>()
                        .Where(r => r.MatchId == mc.MatchId!.Value && r.UserId == userId)
                        .Select(r => (DateTime?)r.LastReadAt)
                        .FirstOrDefault()
                })
                .Where(x => x.LastRead == null || x.CreatedOn > x.LastRead)
                .GroupBy(x => x.MatchId)
                .Select(g => new { MatchId = g.Key, Count = g.Count() })
                .ToListAsync();

            return rows.ToDictionary(x => x.MatchId, x => x.Count);
        }
    }
}