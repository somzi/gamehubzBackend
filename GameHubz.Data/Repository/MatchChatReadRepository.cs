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