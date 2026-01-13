using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    public class UserRoleEntity : BaseEntity
    {
        public UserRoleEntity()
        {
            this.Users = new List<UserEntity>();
            this.DisplayName = String.Empty;
            this.SystemName = String.Empty;
        }

        public string DisplayName { get; set; }

        public string SystemName { get; set; }

        public List<UserEntity> Users { get; }
    }
}
