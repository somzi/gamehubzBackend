using Microsoft.AspNetCore.SignalR;

namespace GameHubz.Logic.SignalR
{
    /// <summary>
    /// Takes users who are no longer allowed into a match chat out of its live group. Joining is
    /// checked once, at <see cref="MatchChatHub.JoinMatchGroup"/>; without this, a player swapped out
    /// of a fixture would keep receiving its messages for as long as they left the chat open.
    /// </summary>
    public class MatchChatAccessRevoker
    {
        private readonly IHubContext<MatchChatHub> hubContext;
        private readonly MatchChatConnectionRegistry registry;

        public MatchChatAccessRevoker(IHubContext<MatchChatHub> hubContext, MatchChatConnectionRegistry registry)
        {
            this.hubContext = hubContext;
            this.registry = registry;
        }

        public async Task RevokeAsync(Guid matchId, IReadOnlyCollection<Guid> userIds)
        {
            if (userIds.Count == 0) return;

            foreach (var connectionId in registry.TakeConnections(matchId, userIds))
                await hubContext.Groups.RemoveFromGroupAsync(connectionId, matchId.ToString());
        }
    }
}
