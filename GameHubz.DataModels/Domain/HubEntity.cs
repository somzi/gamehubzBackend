using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    public class HubEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid UserId { get; set; }
        public UserEntity User { get; set; } = null!;
        public List<UserHubEntity>? UserHubs { get; set; } = new();
        public List<TournamentEntity>? Tournaments { get; set; } = new();
        public List<HubSocialEntity>? HubSocials { get; set; } = new();
        public List<UserHubRequestEntity>? JoinRequests { get; set; } = new();
        public string? AvatarUrl { get; set; }
        public bool IsPublic { get; set; } = true;
        public bool IsVerified { get; set; } = false;

        /// <summary>Discord webhook URL for public tournament announcements. Null = integration off.</summary>
        public string? DiscordWebhookUrl { get; set; }

        /// <summary>JSON per-event toggles (e.g. { "registrationOpened": true, ... }). Null = all events on.</summary>
        public string? DiscordNotificationSettings { get; set; }
    }
}