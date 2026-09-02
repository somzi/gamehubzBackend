using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    /// <summary>
    /// Per-user read cursor for a match chat. A match chat can have several readers
    /// (home/away players, admins stepping in), so unread is tracked with a per-user
    /// "last read" timestamp rather than a per-message IsRead flag (the DM model).
    /// Unread for a user = match messages from someone else created after LastReadAt.
    /// </summary>
    public class MatchChatReadEntity : BaseEntity
    {
        public Guid MatchId { get; set; }

        public MatchEntity? Match { get; set; }

        public Guid UserId { get; set; }

        public UserEntity? User { get; set; }

        public DateTime LastReadAt { get; set; }

        /// <summary>
        /// Per-thread mute. Suppresses push, Discord DM and the aggregate unread badge for this
        /// match while leaving the thread — and its real unread count — visible in the chat inbox,
        /// the way a muted channel behaves elsewhere. Works on any chat, moderated or your own.
        /// </summary>
        public bool IsMuted { get; set; }
    }
}
