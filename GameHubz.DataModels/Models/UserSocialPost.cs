using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Interfaces;

namespace GameHubz.DataModels.Models
{
    public class UserSocialPost : IEditableDto
    {
        public Guid? Id { get; set; }

        public SocialType Type { get; set; }

        public string Username { get; set; } = "";

        public Guid? UserId { get; set; }

        public UserPost? User { get; set; }
    }
}