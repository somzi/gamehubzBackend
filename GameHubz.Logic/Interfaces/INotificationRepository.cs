using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Interfaces
{
    public interface INotificationRepository : IRepository<NotificationEntity>
    {
        /// <summary>Stages new inbox rows (ids pre-assigned by the caller, who needs them for the push). Caller saves.</summary>
        void AddRange(IEnumerable<NotificationEntity> rows);

        /// <summary>
        /// One user's rows, newest first. The cursor is the last row's creation time plus id so rows
        /// sharing the same timestamp are continued rather than skipped at a page boundary.
        /// </summary>
        Task<List<NotificationEntity>> GetPage(
            Guid userId,
            NotificationCategory? category,
            DateTime? before,
            Guid? beforeId,
            int take);

        /// <summary>Unread / unseen counters for each requested user that has at least one unread row.</summary>
        Task<Dictionary<Guid, NotificationSummaryDto>> GetSummaries(IReadOnlyCollection<Guid> userIds);

        Task MarkRead(Guid userId, Guid notificationId, DateTime now);

        Task MarkAllRead(Guid userId, NotificationCategory? category, DateTime now);

        Task MarkSeen(Guid userId, DateTime now);
    }
}
