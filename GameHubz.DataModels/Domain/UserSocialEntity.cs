using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    public class UserSocialEntity : BaseEntity
    {
        public SocialType Type { get; set; }

        public string Username { get; set; } = "";

        public Guid? UserId { get; set; }

        public UserEntity? User { get; set; }
    }
}