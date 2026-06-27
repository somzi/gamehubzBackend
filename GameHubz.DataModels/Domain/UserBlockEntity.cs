using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    public class UserBlockEntity : BaseEntity
    {
        public Guid BlockerId { get; set; }

        public UserEntity? Blocker { get; set; }

        public Guid BlockedId { get; set; }

        public UserEntity? Blocked { get; set; }
    }
}
