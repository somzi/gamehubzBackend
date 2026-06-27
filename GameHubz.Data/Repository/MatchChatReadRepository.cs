using GameHubz.Common.Interfaces;
using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class MatchChatReadRepository : BaseRepository<ApplicationContext, MatchChatReadEntity>, IMatchChatReadRepository
    {
        public MatchChatReadRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task MarkRead(Guid matchId, Guid userId, IUserContextReader userContextReader)
        {
            // The read marker FKs to Match. A stale client can POST /read for a match that was
            // already deleted (e.g. a double-elim cascade delete), and the insert below would blow
            // up with a foreign-key violation (FK_MatchChatRead_Match → 500). There is nothing to
            // mark read on a match that no longer exists, so silently no-op.
            bool matchExists = await this.ContextBase.Set<MatchEntity>()
                .AnyAsync(m => m.Id == matchId);
            if (!matchExists) return;

            // Tracked lookup (not AsNoTracking) so the update is persisted on save.
            var existing = await this.ContextBase.Set<MatchChatReadEntity>()
                .FirstOrDefaultAsync(r => r.MatchId == matchId && r.UserId == userId);

            var now = DateTime.UtcNow;

            if (existing == null)
            {
                var row = new MatchChatReadEntity
                {
                    MatchId = matchId,
                    UserId = userId,
                    LastReadAt = now,
                };
                await this.AddEntity(row, userContextReader);
            }
            else
            {
                existing.LastReadAt = now;
                await this.UpdateEntity(existing, userContextReader);
            }
        }
    }
}