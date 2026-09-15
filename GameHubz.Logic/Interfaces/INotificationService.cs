namespace GameHubz.Logic.Interfaces
{
    public interface INotificationService
    {
        Task SendToOneAsync(string pushToken, string title, string body, object? data = null);

        Task SendToManyAsync(IEnumerable<string> pushTokens, string title, string body, object? data = null);

        /// <summary>
        /// Sends one push per recipient, each written in that recipient's own language.
        /// Recipients are grouped by language so the text is resolved once per language,
        /// not once per device.
        /// </summary>
        Task SendLocalizedToManyAsync(
            IEnumerable<PushRecipient> recipients,
            PushText title,
            PushText body,
            object? data = null);

        /// <summary>Single-recipient convenience over <see cref="SendLocalizedToManyAsync"/>.</summary>
        Task SendLocalizedToOneAsync(
            PushRecipient recipient,
            PushText title,
            PushText body,
            object? data = null);

        /// <summary>
        /// Several notifications at once, each with its own recipients, wording and payload — a sweep that
        /// reminds the players of many matches in one tick. This default sends them one by one;
        /// <see cref="Services.NotificationService"/> writes every inbox row in one save, shares Expo requests
        /// between them and pushes the inbox counters once.
        /// </summary>
        async Task SendLocalizedBatchAsync(IReadOnlyCollection<LocalizedPush> pushes)
        {
            foreach (LocalizedPush push in pushes)
            {
                await this.SendLocalizedToManyAsync(push.Recipients, push.Title, push.Body, push.Data);
            }
        }
    }
}
