using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    /// <summary>
    /// 1-on-1 chat thread between two users. Normalized (UserAId &lt; UserBId).
    /// </summary>
    public class DirectChatEntity : BaseEntity
    {
        public Guid UserAId { get; set; }

        public UserEntity? UserA { get; set; }

        public Guid UserBId { get; set; }

        public UserEntity? UserB { get; set; }

        public string? LastMessage { get; set; }

        public DateTime? LastMessageAt { get; set; }

        public Guid? LastMessageSenderId { get; set; }

        public List<DirectMessageEntity>? Messages { get; set; }
    }
}
