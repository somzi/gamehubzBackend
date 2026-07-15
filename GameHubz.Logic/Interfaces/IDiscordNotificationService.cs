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
        Task SendImageAsync(string webhookUrl, byte[] png, string filename);
    }
}
