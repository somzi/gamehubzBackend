using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class DirectMessageRepository : BaseRepository<ApplicationContext, DirectMessageEntity>, IDirectMessageRepository
    {
        public DirectMessageRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<List<DirectMessageDto>> GetByChatId(Guid chatId, int take = 100, DateTime? before = null)
        {
            var q = this.BaseDbSet().Where(x => x.ChatId == chatId);

            if (before.HasValue)
            {
                q = q.Where(x => x.CreatedOn < before.Value);
            }

            return await q
                .OrderByDescending(x => x.CreatedOn)
                .Take(take)
                .Select(x => new DirectMessageDto
                {
                    Id = x.Id!.Value,
                    ChatId = x.ChatId,
                    SenderId = x.SenderId,
                    SenderUsername = x.Sender!.Username,
                    SenderAvatarUrl = x.Sender!.AvatarUrl,
                    Content = x.Content,
                    SentAt = x.CreatedOn!.Value,
                    IsRead = x.IsRead
                })
                .OrderBy(x => x.SentAt)
                .ToListAsync();
        }

        public async Task MarkRead(Guid chatId, Guid readerUserId)
        {
            var now = DateTime.UtcNow;
            await this.BaseDbSet()
                .Where(x => x.ChatId == chatId && !x.IsRead && x.SenderId != readerUserId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(m => m.IsRead, true)
                    .SetProperty(m => m.ReadAt, now));
        }
    }
}
