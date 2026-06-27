using System.Collections.Generic;
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
    }
}
