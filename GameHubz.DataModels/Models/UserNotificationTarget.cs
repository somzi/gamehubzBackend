namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// Per-user notification channels resolved in one query: the Expo push token (primary channel)
    /// and the linked Discord account for bot DMs (additive channel, honours the user's DM switch).
    /// Every requested user comes back, channels or not — the notification inbox is keyed by
    /// <see cref="UserId"/> and needs no channel at all.
    /// </summary>
    public class UserNotificationTarget
    {
        public Guid UserId { get; set; }

        public string? PushToken { get; set; }

        /// <summary>App language of this user — pushes are written in the RECIPIENT's language.</summary>
        public string? Language { get; set; }

        public string? DiscordUserId { get; set; }

        public bool DiscordDmEnabled { get; set; }
    }
}
