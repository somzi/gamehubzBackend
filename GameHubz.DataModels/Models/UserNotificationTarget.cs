namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// Per-user notification channels resolved in one query: the Expo push token (primary channel)
    /// and the linked Discord account for bot DMs (additive channel, honours the user's DM switch).
    /// </summary>
    public class UserNotificationTarget
    {
        public string? PushToken { get; set; }

        public string? DiscordUserId { get; set; }

        public bool DiscordDmEnabled { get; set; }
    }
}
