namespace GameHubz.DataModels.Models
{
    public class HubBanOverview
    {
        public Guid UserId { get; set; }

        public string Username { get; set; } = string.Empty;

        public string? AvatarUrl { get; set; }

        public Guid? BannedById { get; set; }

        public string? BannedByName { get; set; }

        public DateTime? BannedAt { get; set; }
    }
}
