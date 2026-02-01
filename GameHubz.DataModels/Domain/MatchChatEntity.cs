using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    public class MatchChatEntity : BaseEntity
    {
        public string Content { get; set; } = "";

        public Guid? MatchId { get; set; }

        public MatchEntity? Match { get; set; }

        public Guid? UserId { get; set; }

        public UserEntity? User { get; set; }
    }
}