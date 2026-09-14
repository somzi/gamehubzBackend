using GameHubz.Common.Interfaces;
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
    public class NotificationRepository : BaseRepository<ApplicationContext, NotificationEntity>, INotificationRepository
    {
        public NotificationRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        // Straight onto the context rather than through AddEntity: these rows are written by the server
        // on nobody's behalf — usually from a background send that has no request user at all — so there
        // is no CreatedBy to stamp. The unit of work still stamps CreatedOn / ModifiedOn on save.
        public void AddRange(IEnumerable<NotificationEntity> rows)
        {
            foreach (var row in rows)
            {
                if (row.Id == null || row.Id == Guid.Empty)
                {
                    row.Id = Guid.NewGuid();
                }

                this.ContextBase.Entry(row).State = EntityState.Added;
            }
        }

        public async Task<List<NotificationEntity>> GetPage(Guid userId, NotificationCategory? category, DateTime? before, int take)
        {
            var query = this.BaseDbSet().Where(n => n.UserId == userId);

            if (category.HasValue)
            {
                query = query.Where(n => n.Category == category.Value);
            }

            if (before.HasValue)
            {
                query = query.Where(n => n.CreatedOn < before.Value);
            }

            return await query
                .OrderByDescending(n => n.CreatedOn)
                .ThenByDescending(n => n.Id)
                .Take(take)
                .ToListAsync();
        }

        public async Task<Dictionary<Guid, NotificationSummaryDto>> GetSummaries(IReadOnlyCollection<Guid> userIds)
        {
            var result = new Dictionary<Guid, NotificationSummaryDto>();
            if (userIds.Count == 0)
            {
                return result;
            }

            var ids = userIds.ToList();

            // One grouped pass over the unread rows only (IX_Notification_User_Unread), for one user or
            // for every recipient of a broadcast alike.
            var rows = await this.BaseDbSet()
                .Where(n => ids.Contains(n.UserId) && n.ReadOn == null)
                .GroupBy(n => new { n.UserId, n.Category })
                .Select(g => new
                {
                    g.Key.UserId,
                    g.Key.Category,
                    Unread = g.Count(),
                    Unseen = g.Sum(n => n.SeenOn == null ? 1 : 0),
                })
                .ToListAsync();

            foreach (var row in rows)
            {
                if (!result.TryGetValue(row.UserId, out var summary))
                {
                    summary = new NotificationSummaryDto();
                    result[row.UserId] = summary;
                }

                if (row.Category == NotificationCategory.Action)
                {
                    summary.UnreadActions += row.Unread;
                }
                else
                {
                    summary.UnreadUpdates += row.Unread;
                }

                summary.Unseen += row.Unseen;
            }

            return result;
        }

        // Scoped to the caller's own rows by the UserId in the WHERE: an id belonging to someone else
        // simply matches nothing, so no separate ownership check is needed.
        public Task MarkRead(Guid userId, Guid notificationId, DateTime now)
        {
            return this.BaseDbSet()
                .Where(n => n.Id == notificationId && n.UserId == userId && n.ReadOn == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.ReadOn, now)
                    .SetProperty(n => n.SeenOn, n => (DateTime?)(n.SeenOn ?? now))
                    .SetProperty(n => n.ModifiedOn, now));
        }

        public Task MarkAllRead(Guid userId, NotificationCategory? category, DateTime now)
        {
            var query = this.BaseDbSet().Where(n => n.UserId == userId && n.ReadOn == null);

            if (category.HasValue)
            {
                query = query.Where(n => n.Category == category.Value);
            }

            return query.ExecuteUpdateAsync(s => s
                .SetProperty(n => n.ReadOn, now)
                .SetProperty(n => n.SeenOn, n => (DateTime?)(n.SeenOn ?? now))
                .SetProperty(n => n.ModifiedOn, now));
        }

        // Read rows are left alone: they are already out of every counter.
        public Task MarkSeen(Guid userId, DateTime now)
        {
            return this.BaseDbSet()
                .Where(n => n.UserId == userId && n.ReadOn == null && n.SeenOn == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.SeenOn, now)
                    .SetProperty(n => n.ModifiedOn, now));
        }
    }
}
