using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    public class UserHubBanEntity : BaseEntity
    {
        public Guid? UserId { get; set; }

        public UserEntity? User { get; set; }

        public Guid? HubId { get; set; }

        public HubEntity? Hub { get; set; }

        public Guid? BannedById { get; set; }
    }
}
