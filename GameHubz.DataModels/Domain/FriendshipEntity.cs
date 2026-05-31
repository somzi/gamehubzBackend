using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    /// <summary>
    /// Mutual friendship between two users. Stored once with normalized order
    /// (UserAId &lt; UserBId by Guid comparison) to keep lookups O(1).
    /// </summary>
    public class FriendshipEntity : BaseEntity
    {
        public Guid UserAId { get; set; }

        public UserEntity? UserA { get; set; }

        public Guid UserBId { get; set; }

        public UserEntity? UserB { get; set; }
    }
}
