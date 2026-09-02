using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameHubz.Logic.Interfaces;

namespace GameHubz.Logic.Test.Bracket
{
    /// <summary>
    /// Records push-notification calls instead of sending them, so tests can assert that the
    /// "tournament is live" broadcast fired without standing up a real push backend.
    /// </summary>
    internal sealed class FakeNotificationService : INotificationService
    {
        public List<(string Token, string Title, string Body)> Sent { get; } = new();

        public Task SendToOneAsync(string pushToken, string title, string body, object? data = null)
        {
            Sent.Add((pushToken, title, body));
            return Task.CompletedTask;
        }

        public Task SendToManyAsync(IEnumerable<string> pushTokens, string title, string body, object? data = null)
        {
            foreach (var token in pushTokens)
            {
                Sent.Add((token, title, body));
            }

            return Task.CompletedTask;
        }

        public Task SendLocalizedToOneAsync(PushRecipient recipient, PushText title, PushText body, object? data = null)
        {
            Sent.Add((recipient.PushToken, Describe(title), Describe(body)));
            return Task.CompletedTask;
        }

        public Task SendLocalizedToManyAsync(IEnumerable<PushRecipient> recipients, PushText title, PushText body, object? data = null)
        {
            foreach (var recipient in recipients)
            {
                Sent.Add((recipient.PushToken, Describe(title), Describe(body)));
            }

            return Task.CompletedTask;
        }

        // The resource key, not the rendered sentence — asserting on the key is what tells a test
        // that the right notification fired, and it survives any later rewording of the copy.
        private static string Describe(PushText text)
            => text.ResourceKey ?? text.Literal ?? string.Empty;
    }
}
