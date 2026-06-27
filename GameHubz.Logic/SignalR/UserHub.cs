using GameHubz.DataModels.Tokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GameHubz.Logic.SignalR
{
    /// <summary>
    /// Per-user SignalR hub used for ambient, cross-screen pushes (notification badges).
    /// On connect the client is placed in a group keyed by its own user id, so the server
    /// can push badge updates to every device a user has open, from any screen.
    /// The JWT is supplied as the "access_token" query-string parameter (WebSockets can't
    /// set Authorization headers) — see AuthenticationStartup.OnMessageReceived.
    /// </summary>
    [Authorize]
    public class UserHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var idClaim = Context.User?.FindFirst(JwtClaimIdentifiers.Id)?.Value;
            if (Guid.TryParse(idClaim, out var userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(userId));
            }

            await base.OnConnectedAsync();
        }

        public static string GroupName(Guid userId) => $"user:{userId}";
    }
}
