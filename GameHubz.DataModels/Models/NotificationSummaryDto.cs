namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// Inbox counters for the signed-in user. Returned by GET api/v2/notifications/summary and by every
    /// inbox write, and pushed live through the UserHub ("NotificationsUpdated") whenever they change.
    /// </summary>
    public class NotificationSummaryDto
    {
        /// <summary>Unread rows that arrived since the user last opened the inbox — the bell badge.</summary>
        public int Unseen { get; set; }

        public int UnreadActions { get; set; }

        public int UnreadUpdates { get; set; }

        public int Unread => UnreadActions + UnreadUpdates;
    }
}
