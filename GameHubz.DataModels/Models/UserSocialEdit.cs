using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class UserSocialEdit
    {
        public Guid? Id { get; set; }

        public SocialType Type { get; set; }

        public string Username { get; set; } = "";

        public Guid? UserId { get; set; }

        public UserEdit? User { get; set; }
    }
}