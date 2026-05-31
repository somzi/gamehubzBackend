using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    public class FriendRequestEntity : BaseEntity
    {
        public Guid FromUserId { get; set; }

        public UserEntity? FromUser { get; set; }

        public Guid ToUserId { get; set; }

        public UserEntity? ToUser { get; set; }

        public FriendRequestStatus Status { get; set; }
    }
}
