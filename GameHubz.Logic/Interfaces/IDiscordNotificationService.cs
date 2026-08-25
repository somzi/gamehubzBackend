namespace GameHubz.Logic.Interfaces
{
    /// <summary>
    /// Dumb Discord webhook transport: POSTs a rendered announcement card (PNG) to a webhook URL.
    /// Knows nothing about tournaments or matches — cards come from
    /// <see cref="Services.DiscordAnnouncementCard"/>, event routing lives in the notifiers.
    /// Never throws: a Discord failure is logged and swallowed.
    /// </summary>
    public interface IDiscordNotificationService
    {
        /// <param name="content">
        /// Optional message text posted above the card. Used for the one thing a rendered PNG
        /// cannot do: Discord's <c>&lt;t:unix:F&gt;</c> markers, which every reader sees in their
        /// own timezone. Null/empty posts the card alone, exactly as before.
        /// </param>
        Task SendImageAsync(string webhookUrl, byte[] png, string filename, string? content = null);
    }
}
