using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Interfaces;

namespace GameHubz.DataModels.Models
{
    public class HubSocialPost : IEditableDto
    {
        public Guid? Id { get; set; }

        public string Username { get; set; } = "";

        public SocialType Type { get; set; }

        public Guid? HubId { get; set; }
    }
}