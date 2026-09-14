using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    /// <summary>
    /// One entry in a user's notification inbox: a notification GameHubz sent them, stored exactly as
    /// it was worded for them. Written by NotificationService at send time — including for users with
    /// no push token, for whom the inbox is the only place the notification ever shows up.
    /// </summary>
    public class NotificationEntity : BaseEntity
    {
        public Guid UserId { get; set; }

        public UserEntity? User { get; set; }

        /// <summary>The payload's <c>type</c> as sent (e.g. "roundDeadline"); null for an untyped push.</summary>
        public string? Type { get; set; }

        public NotificationCategory Category { get; set; }

        /// <summary>Already resolved in the recipient's language — never re-translated on read.</summary>
        public string Title { get; set; } = "";

        public string Body { get; set; } = "";

        /// <summary>The push's data payload (camelCase JSON) — what the app routes a tap with.</summary>
        public string? DataJson { get; set; }

        /// <summary>Stamped when the user opens the inbox. Unread rows not yet seen drive the bell badge.</summary>
        public DateTime? SeenOn { get; set; }

        public DateTime? ReadOn { get; set; }
    }
}
