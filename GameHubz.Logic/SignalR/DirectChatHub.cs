using Microsoft.AspNetCore.SignalR;

namespace GameHubz.Logic.SignalR
{
    /// <summary>
    /// SignalR hub for 1-on-1 direct messages. Mobile clients join a group keyed
    /// by chat id when they open a chat screen and leave it when they navigate away.
    /// </summary>
    public class DirectChatHub : Hub
    {
        public async Task JoinChatGroup(string chatId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, ChatGroupName(chatId));
        }

        public async Task LeaveChatGroup(string chatId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, ChatGroupName(chatId));
        }

        public static string ChatGroupName(string chatId) => $"dm:{chatId}";

        public static string ChatGroupName(Guid chatId) => ChatGroupName(chatId.ToString());
    }
}
