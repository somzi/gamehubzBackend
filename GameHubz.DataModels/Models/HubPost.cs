using GameHubz.DataModels.Interfaces;

namespace GameHubz.DataModels.Models
{
    public class HubPost : IEditableDto
    {
        public Guid? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsPublic { get; set; } = true;
        public string? DiscordWebhookUrl { get; set; }
        public string? DiscordNotificationSettings { get; set; }
    }
}