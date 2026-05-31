using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    public class UserHubRequestEntity : BaseEntity
    {
        public Guid? HubId { get; set; }
        public HubEntity? Hub { get; set; }
        public Guid? UserId { get; set; }
        public UserEntity? User { get; set; }
        public JoinRequestStatus Status { get; set; }
    }
}
