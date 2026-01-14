using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class UserProfileDto
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public RegionType Region { get; set; }
        public List<UserSocialDto>? UserSocials { get; set; } = new();
    }
}