using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    public class DirectMessageEntity : BaseEntity
    {
        public Guid ChatId { get; set; }

        public DirectChatEntity? Chat { get; set; }

        public Guid SenderId { get; set; }

        public UserEntity? Sender { get; set; }

        public string Content { get; set; } = "";

        public bool IsRead { get; set; }

        public DateTime? ReadAt { get; set; }
    }
}
