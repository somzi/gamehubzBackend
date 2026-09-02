using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class UserHubOverview
    {
        public string Username { get; set; } = string.Empty;
        public Guid UserId { get; set; }
        public string? PushToken { get; set; }

        /// <summary>App language of this member — pushes are written in the RECIPIENT's language.</summary>
        public string? Language { get; set; }
        public string? AvatarUrl { get; set; }
        public HubRole HubRole { get; set; }
    }
}
