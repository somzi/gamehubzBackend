using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class HubSocialDto
    {
        public Guid? Id { get; set; }
        public string Username { get; set; } = "";

        public SocialType Type { get; set; }

        public Guid? HubId { get; set; }

        public HubDto? Hub { get; set; }
    }
}