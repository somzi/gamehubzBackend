namespace GameHubz.Logic.Interfaces
{
    /// <summary>
    /// Dumb Discord webhook transport: POSTs a ready-made embed payload to a webhook URL.
    /// Knows nothing about tournaments or matches — payloads come from
    /// <see cref="Services.DiscordEmbedBuilder"/>, event routing lives in the notifiers.
    /// Never throws: a Discord failure is logged and swallowed.
    /// </summary>
    public interface IDiscordNotificationService
    {
        Task SendAsync(string webhookUrl, object payload);
    }
}
