using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class FriendService : AppBaseService
    {
        private readonly INotificationService notificationService;

        public FriendService(
            IUnitOfWorkFactory factory,
            ILocalizationService localizationService,
            IUserContextReader userContextReader,
            INotificationService notificationService)
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.notificationService = notificationService;
        }

        // ─────────────────────────────────────────────────────────────────
        // QUERIES
        // ─────────────────────────────────────────────────────────────────

        public async Task<List<FriendDto>> GetMyFriends(string? search)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            return await this.AppUnitOfWork.FriendshipRepository.GetFriendsOf(user.UserId, search);
        }

        public async Task<List<FriendRequestDto>> GetIncomingRequests(string? search)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            return await this.AppUnitOfWork.FriendRequestRepository.GetIncomingPending(user.UserId, search);
        }

        public async Task<List<FriendRequestDto>> GetOutgoingRequests(string? search)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            return await this.AppUnitOfWork.FriendRequestRepository.GetOutgoingPending(user.UserId, search);
        }

        public async Task<List<BlockedUserDto>> GetBlocked(string? search)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            return await this.AppUnitOfWork.UserBlockRepository.GetBlockedList(user.UserId, search);
        }

        public async Task<FriendRelationStatusDto> GetRelationStatus(Guid otherUserId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var result = new FriendRelationStatusDto { OtherUserId = otherUserId };

            if (otherUserId == user.UserId)
            {
                result.Status = FriendRelationStatus.Self;
                return result;
            }

            if (await this.AppUnitOfWork.UserBlockRepository.IsBlocked(user.UserId, otherUserId))
            {
                result.Status = FriendRelationStatus.BlockedByMe;
                return result;
            }

            if (await this.AppUnitOfWork.UserBlockRepository.IsBlocked(otherUserId, user.UserId))
            {
                result.Status = FriendRelationStatus.BlockedByOther;
                return result;
            }

            if (await this.AppUnitOfWork.FriendshipRepository.AreFriends(user.UserId, otherUserId))
            {
                result.Status = FriendRelationStatus.Friends;
                return result;
            }

            var pending = await this.AppUnitOfWork.FriendRequestRepository.FindPendingBetween(user.UserId, otherUserId);
            if (pending != null)
            {
                result.RequestId = pending.Id;
                result.Status = pending.FromUserId == user.UserId
                    ? FriendRelationStatus.OutgoingRequest
                    : FriendRelationStatus.IncomingRequest;
                return result;
            }

            result.Status = FriendRelationStatus.None;
            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        // COMMANDS
        // ─────────────────────────────────────────────────────────────────

        public async Task<FriendRequestDto> SendRequest(Guid toUserId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            if (toUserId == user.UserId)
                throw new Exception("You cannot send a friend request to yourself.");

            var target = await this.AppUnitOfWork.UserRepository.GetById(toUserId);
            if (target == null)
                throw new Exception("User not found.");

            if (await this.AppUnitOfWork.UserBlockRepository.EitherBlocks(user.UserId, toUserId))
                throw new Exception("Cannot send a request — there is an active block between the users.");

            if (await this.AppUnitOfWork.FriendshipRepository.AreFriends(user.UserId, toUserId))
                throw new Exception("You are already friends.");

            var existing = await this.AppUnitOfWork.FriendRequestRepository.FindPendingBetween(user.UserId, toUserId);
            if (existing != null)
            {
                // If the other party already sent a request, just accept it.
                if (existing.FromUserId == toUserId)
                {
                    await AcceptRequest(existing.Id!.Value);
                    return MapToDto(existing, target.Username, target.Nickname, target.AvatarUrl,
                                            user.Username, null, null);
                }

                throw new Exception("A pending request already exists.");
            }

            var request = new FriendRequestEntity
            {
                FromUserId = user.UserId,
                ToUserId = toUserId,
                Status = FriendRequestStatus.Pending,
            };

            await this.AppUnitOfWork.FriendRequestRepository.AddEntity(request, this.UserContextReader);
            await this.SaveAsync();

            // Fire-and-forget push notification
            SendNotification(target, user.Username, "New friend request",
                $"{user.Username} sent you a friend request.",
                new { type = "friend_request", requestId = request.Id!.Value.ToString(), fromUserId = user.UserId.ToString() });

            return new FriendRequestDto
            {
                Id = request.Id!.Value,
                FromUserId = user.UserId,
                FromUsername = user.Username,
                ToUserId = toUserId,
                ToUsername = target.Username,
                ToNickname = target.Nickname,
                ToAvatarUrl = target.AvatarUrl,
                Status = FriendRequestStatus.Pending,
                CreatedOn = request.CreatedOn!.Value
            };
        }

        public async Task AcceptRequest(Guid requestId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var request = await this.AppUnitOfWork.FriendRequestRepository.GetById(requestId);
            if (request == null)
                throw new Exception("Friend request not found.");

            if (request.ToUserId != user.UserId)
                throw new Exception("Only the recipient can accept this request.");

            if (request.Status != FriendRequestStatus.Pending)
                throw new Exception("This request is no longer pending.");

            request.Status = FriendRequestStatus.Accepted;
            await this.AppUnitOfWork.FriendRequestRepository.UpdateEntity(request, this.UserContextReader);

            // Create the friendship (normalized order). If a soft-deleted row
            // exists for this pair, resurrect it — UQ_Friendship_Pair is not
            // partial, so re-inserting would violate the unique constraint.
            var (a, b) = SocialPair.Normalize(request.FromUserId, request.ToUserId);
            var existing = await this.AppUnitOfWork.FriendshipRepository.FindIncludingDeleted(a, b);
            if (existing == null)
            {
                var friendship = new FriendshipEntity { UserAId = a, UserBId = b };
                await this.AppUnitOfWork.FriendshipRepository.AddEntity(friendship, this.UserContextReader);
            }
            else if (existing.IsDeleted)
            {
                existing.IsDeleted = false;
                await this.AppUnitOfWork.FriendshipRepository.UpdateEntity(existing, this.UserContextReader);
            }

            await this.SaveAsync();

            // Notify the original sender
            var sender = await this.AppUnitOfWork.UserRepository.GetById(request.FromUserId);
            SendNotification(sender, user.Username, "Friend request accepted",
                $"{user.Username} accepted your friend request.",
                new { type = "friend_accepted", userId = user.UserId.ToString() });
        }

        public async Task RejectRequest(Guid requestId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var request = await this.AppUnitOfWork.FriendRequestRepository.GetById(requestId);
            if (request == null)
                throw new Exception("Friend request not found.");

            if (request.ToUserId != user.UserId)
                throw new Exception("Only the recipient can reject this request.");

            if (request.Status != FriendRequestStatus.Pending)
                throw new Exception("This request is no longer pending.");

            request.Status = FriendRequestStatus.Rejected;
            await this.AppUnitOfWork.FriendRequestRepository.UpdateEntity(request, this.UserContextReader);
            await this.SaveAsync();
        }

        public async Task CancelRequest(Guid requestId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var request = await this.AppUnitOfWork.FriendRequestRepository.GetById(requestId);
            if (request == null)
                throw new Exception("Friend request not found.");

            if (request.FromUserId != user.UserId)
                throw new Exception("Only the sender can cancel this request.");

            if (request.Status != FriendRequestStatus.Pending)
                throw new Exception("This request is no longer pending.");

            request.Status = FriendRequestStatus.Cancelled;
            await this.AppUnitOfWork.FriendRequestRepository.UpdateEntity(request, this.UserContextReader);
            await this.SaveAsync();
        }

        public async Task Unfriend(Guid otherUserId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var friendship = await this.AppUnitOfWork.FriendshipRepository.Find(user.UserId, otherUserId);
            if (friendship == null)
                throw new Exception("You are not friends with this user.");

            await this.AppUnitOfWork.FriendshipRepository.SoftDeleteEntity(friendship, this.UserContextReader);
            await this.SaveAsync();
        }

        public async Task Block(Guid otherUserId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            if (otherUserId == user.UserId)
                throw new Exception("You cannot block yourself.");

            var target = await this.AppUnitOfWork.UserRepository.GetById(otherUserId);
            if (target == null)
                throw new Exception("User not found.");

            // If a soft-deleted block row exists for this pair, resurrect it —
            // UQ_UserBlock_Pair is not partial, so re-inserting would violate it.
            var existing = await this.AppUnitOfWork.UserBlockRepository.FindIncludingDeleted(user.UserId, otherUserId);
            if (existing != null && !existing.IsDeleted)
                return; // already blocked

            if (existing != null)
            {
                existing.IsDeleted = false;
                await this.AppUnitOfWork.UserBlockRepository.UpdateEntity(existing, this.UserContextReader);
            }
            else
            {
                var block = new UserBlockEntity
                {
                    BlockerId = user.UserId,
                    BlockedId = otherUserId,
                };

                await this.AppUnitOfWork.UserBlockRepository.AddEntity(block, this.UserContextReader);
            }

            // Soft-delete any existing friendship between the two
            var friendship = await this.AppUnitOfWork.FriendshipRepository.Find(user.UserId, otherUserId);
            if (friendship != null)
            {
                await this.AppUnitOfWork.FriendshipRepository.SoftDeleteEntity(friendship, this.UserContextReader);
            }

            // Cancel any pending requests in either direction
            var pending = await this.AppUnitOfWork.FriendRequestRepository.FindPendingBetween(user.UserId, otherUserId);
            if (pending != null)
            {
                pending.Status = FriendRequestStatus.Cancelled;
                await this.AppUnitOfWork.FriendRequestRepository.UpdateEntity(pending, this.UserContextReader);
            }

            await this.SaveAsync();
        }

        public async Task Unblock(Guid otherUserId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var block = await this.AppUnitOfWork.UserBlockRepository.Find(user.UserId, otherUserId);
            if (block == null)
                return;

            await this.AppUnitOfWork.UserBlockRepository.SoftDeleteEntity(block, this.UserContextReader);
            await this.SaveAsync();
        }

        // ─────────────────────────────────────────────────────────────────
        // PRIVATE
        // ─────────────────────────────────────────────────────────────────

        private void SendNotification(UserEntity? target, string fromUsername, string title, string body, object data)
        {
            if (target?.PushToken == null) return;

            string token = target.PushToken;
            _ = Task.Run(async () =>
            {
                try
                {
                    await notificationService.SendToOneAsync(token, title, body, data);
                }
                catch { /* swallow */ }
            });
        }

        private static FriendRequestDto MapToDto(
            FriendRequestEntity r, string toUsername, string? toNick, string? toAvatar,
            string fromUsername, string? fromNick, string? fromAvatar)
        {
            return new FriendRequestDto
            {
                Id = r.Id!.Value,
                FromUserId = r.FromUserId,
                FromUsername = fromUsername,
                FromNickname = fromNick,
                FromAvatarUrl = fromAvatar,
                ToUserId = r.ToUserId,
                ToUsername = toUsername,
                ToNickname = toNick,
                ToAvatarUrl = toAvatar,
                Status = r.Status,
                CreatedOn = r.CreatedOn!.Value,
            };
        }
    }
}
