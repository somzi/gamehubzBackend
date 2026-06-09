using GameHubz.Logic.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace GameHubz.Logic.Services
{
    public class DirectChatService : AppBaseService
    {
        private readonly IHubContext<DirectChatHub> hubContext;
        private readonly INotificationService notificationService;
        private readonly FriendService friendService;
        private readonly ICacheService cacheService;

        public DirectChatService(
            IUnitOfWorkFactory factory,
            ILocalizationService localizationService,
            IUserContextReader userContextReader,
            IHubContext<DirectChatHub> hubContext,
            INotificationService notificationService,
            FriendService friendService,
            ICacheService cacheService)
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.hubContext = hubContext;
            this.notificationService = notificationService;
            this.friendService = friendService;
            this.cacheService = cacheService;
        }

        // ─────────────────────────────────────────────────────────────────
        // QUERIES
        // ─────────────────────────────────────────────────────────────────

        public async Task<List<DirectChatDto>> GetMyChats(string? search)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            // Only the no-search variant is cached — search variants would create
            // unbounded keys.
            if (!string.IsNullOrWhiteSpace(search))
                return await this.AppUnitOfWork.DirectChatRepository.GetChatsForUser(user.UserId, search);

            string key = $"direct_chats:{user.UserId}";
            var cached = await this.cacheService.GetAsync<List<DirectChatDto>>(key);
            if (cached != null) return cached;

            var list = await this.AppUnitOfWork.DirectChatRepository.GetChatsForUser(user.UserId, null);
            await this.cacheService.SetAsync(key, list, TimeSpan.FromSeconds(30));
            return list;
        }

        public async Task<DirectChatDto> GetOrCreateChat(Guid otherUserId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            if (otherUserId == user.UserId)
                throw new Exception("You cannot chat with yourself.");

            if (await this.friendService.EitherBlocksCachedAsync(user.UserId, otherUserId))
                throw new Exception("Cannot open chat — there is an active block between the users.");

            var other = await this.AppUnitOfWork.UserRepository.GetById(otherUserId);
            if (other == null)
                throw new Exception("User not found.");

            var chat = await this.AppUnitOfWork.DirectChatRepository.Find(user.UserId, otherUserId);
            if (chat == null)
            {
                var (a, b) = SocialPair.Normalize(user.UserId, otherUserId);
                chat = new DirectChatEntity { UserAId = a, UserBId = b };
                await this.AppUnitOfWork.DirectChatRepository.AddEntity(chat, this.UserContextReader);
                await this.SaveAsync();

                // A new chat row was created — both participants' cached chat lists are stale.
                await this.cacheService.RemoveAsync($"direct_chats:{user.UserId}");
                await this.cacheService.RemoveAsync($"direct_chats:{otherUserId}");
            }

            return new DirectChatDto
            {
                Id = chat.Id!.Value,
                OtherUserId = otherUserId,
                OtherUsername = other.Username,
                OtherNickname = other.Nickname,
                OtherAvatarUrl = other.AvatarUrl,
                LastMessage = chat.LastMessage,
                LastMessageAt = chat.LastMessageAt,
                LastMessageSenderId = chat.LastMessageSenderId,
                UnreadCount = 0,
            };
        }

        public async Task<List<DirectMessageDto>> GetMessages(Guid chatId, int take = 100, DateTime? before = null)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var chat = await this.AppUnitOfWork.DirectChatRepository.GetByIdForUser(chatId, user.UserId);
            if (chat == null)
                throw new Exception("Chat not found.");

            return await this.AppUnitOfWork.DirectMessageRepository.GetByChatId(chatId, take, before);
        }

        // ─────────────────────────────────────────────────────────────────
        // COMMANDS
        // ─────────────────────────────────────────────────────────────────

        public async Task<DirectMessageDto> SendMessage(Guid chatId, string content)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            if (string.IsNullOrWhiteSpace(content))
                throw new Exception("Message cannot be empty.");

            var chat = await this.AppUnitOfWork.DirectChatRepository.GetByIdForUser(chatId, user.UserId);
            if (chat == null)
                throw new Exception("Chat not found.");

            Guid otherUserId = chat.UserAId == user.UserId ? chat.UserBId : chat.UserAId;

            if (await this.friendService.EitherBlocksCachedAsync(user.UserId, otherUserId))
                throw new Exception("Cannot send message — there is an active block between the users.");

            if (!await this.friendService.AreFriendsCachedAsync(user.UserId, otherUserId))
                throw new Exception("Cannot send message — you are no longer friends.");

            var message = new DirectMessageEntity
            {
                ChatId = chatId,
                SenderId = user.UserId,
                Content = content,
                IsRead = false,
            };

            await this.AppUnitOfWork.DirectMessageRepository.AddEntity(message, this.UserContextReader);

            chat.LastMessage = content.Length > 500 ? content.Substring(0, 500) : content;
            chat.LastMessageAt = DateTime.UtcNow;
            chat.LastMessageSenderId = user.UserId;
            await this.AppUnitOfWork.DirectChatRepository.UpdateEntity(chat, this.UserContextReader);

            await this.SaveAsync();

            var sender = await this.AppUnitOfWork.UserRepository.GetById(user.UserId);

            var dto = new DirectMessageDto
            {
                Id = message.Id!.Value,
                ChatId = chatId,
                SenderId = user.UserId,
                SenderUsername = user.Username,
                SenderAvatarUrl = sender?.AvatarUrl,
                Content = content,
                SentAt = message.CreatedOn!.Value,
                IsRead = false,
            };

            // Broadcast to anyone in the chat group (both participants if currently
            // viewing the chat). The receiver's screen reacts in real time.
            await hubContext.Clients.Group(DirectChatHub.ChatGroupName(chatId))
                .SendAsync("ReceiveMessage", dto);

            // The DM list shows LastMessage / LastMessageAt / UnreadCount — all change here,
            // for both participants.
            await this.cacheService.RemoveAsync($"direct_chats:{user.UserId}");
            await this.cacheService.RemoveAsync($"direct_chats:{otherUserId}");

            // Push notification to the OTHER user
            SendPushNotification(otherUserId, user.Username, content, chatId);

            return dto;
        }

        public async Task MarkRead(Guid chatId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var chat = await this.AppUnitOfWork.DirectChatRepository.GetByIdForUser(chatId, user.UserId);
            if (chat == null)
                throw new Exception("Chat not found.");

            await this.AppUnitOfWork.DirectMessageRepository.MarkRead(chatId, user.UserId);

            // Inform the chat group so the sender's screen can update read receipts.
            await hubContext.Clients.Group(DirectChatHub.ChatGroupName(chatId))
                .SendAsync("MessagesRead", new { chatId, readerUserId = user.UserId });

            // UnreadCount changes for the reader; the sender's last-message read-receipt
            // indicator may also depend on this state. Invalidate both for safety.
            Guid otherUserId = chat.UserAId == user.UserId ? chat.UserBId : chat.UserAId;
            await this.cacheService.RemoveAsync($"direct_chats:{user.UserId}");
            await this.cacheService.RemoveAsync($"direct_chats:{otherUserId}");
        }

        // ─────────────────────────────────────────────────────────────────
        // PRIVATE
        // ─────────────────────────────────────────────────────────────────

        private void SendPushNotification(Guid recipientUserId, string senderUsername, string content, Guid chatId)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var recipient = await this.AppUnitOfWork.UserRepository.GetById(recipientUserId);
                    if (recipient?.PushToken == null) return;

                    string body = content.Length > 120 ? content.Substring(0, 117) + "..." : content;

                    await notificationService.SendToOneAsync(
                        recipient.PushToken,
                        senderUsername,
                        body,
                        new
                        {
                            type = "direct_message",
                            chatId = chatId.ToString(),
                        });
                }
                catch { /* swallow */ }
            });
        }
    }
}
