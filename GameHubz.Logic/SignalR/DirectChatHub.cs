using GameHubz.DataModels.Tokens;
using GameHubz.Logic.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GameHubz.Logic.SignalR
{
    /// <summary>
    /// SignalR hub for 1-on-1 direct messages. Mobile clients join a group keyed
    /// by chat id when they open a chat screen and leave it when they navigate away.
    /// The JWT is supplied as the "access_token" query-string parameter (WebSockets can't
    /// set Authorization headers) — see AuthenticationStartup.OnMessageReceived.
    /// </summary>
    [Authorize]
    public class DirectChatHub : Hub
    {
        private readonly IUnitOfWorkFactory unitOfWorkFactory;

        public DirectChatHub(IUnitOfWorkFactory unitOfWorkFactory)
        {
            this.unitOfWorkFactory = unitOfWorkFactory;
        }

        public async Task JoinChatGroup(string chatId)
        {
            // F94: a connection may only join a chat it is a participant of. Without this check any
            // authenticated user could join an arbitrary/guessed chat id and receive its private
            // messages. The user id comes from the validated token, never from the client.
            var userId = GetUserId();

            if (!Guid.TryParse(chatId, out var chatGuid))
                throw new HubException("Invalid chat id.");

            var chat = await this.unitOfWorkFactory.CreateAppUnitOfWork()
                .DirectChatRepository.GetByIdForUser(chatGuid, userId);

            if (chat == null)
                throw new HubException("You are not a participant of this chat.");

            await Groups.AddToGroupAsync(Context.ConnectionId, ChatGroupName(chatId));
        }

        public async Task LeaveChatGroup(string chatId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, ChatGroupName(chatId));
        }

        public static string ChatGroupName(string chatId) => $"dm:{chatId}";

        public static string ChatGroupName(Guid chatId) => ChatGroupName(chatId.ToString());

        private Guid GetUserId()
        {
            var idClaim = Context.User?.FindFirst(JwtClaimIdentifiers.Id)?.Value;
            if (!Guid.TryParse(idClaim, out var userId))
                throw new HubException("Unauthenticated.");

            return userId;
        }
    }
}
