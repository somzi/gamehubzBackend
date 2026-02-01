using Microsoft.AspNetCore.SignalR;

namespace GameHubz.Logic.SignalR
{
    public class MatchChatHub : Hub
    {
        // Frontend zove ovu metodu kad uđe na ekran meča
        public async Task JoinMatchGroup(string matchId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, matchId);
        }

        // Frontend zove ovo kad izađe sa ekrana (clean-up)
        public async Task LeaveMatchGroup(string matchId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, matchId);
        }
    }
}