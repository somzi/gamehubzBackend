using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class FriendService : AppBaseService
    {
        private readonly INotificationService notificationService;
        private readonly ICacheService cacheService;
        private readonly BadgeService badgeService;

        // Friends_set / blocks_in / blocks_out TTL — long enough to coast through the
        // common burst of relation checks during a chat session, short enough that any
        // missed invalidation self-heals within a few minutes.
        private static readonly TimeSpan SocialSetTtl = TimeSpan.FromMinutes(5);

        public FriendService(
            IUnitOfWorkFactory factory,
            ILocalizationService localizationService,
            IUserContextReader userContextReader,
            INotificationService notificationService,
            ICacheService cacheService,
            BadgeService badgeService)
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.notificationService = notificationService;
            this.cacheService = cacheService;
            this.badgeService = badgeService;
        }

        // ─────────────────────────────────────────────────────────────────
        // CACHED LOOKUPS — used by FriendService itself and by DirectChatService
        // ─────────────────────────────────────────────────────────────────

        public async Task<bool> AreFriendsCachedAsync(Guid userA, Guid userB)
        {
            var set = await GetFriendsSetAsync(userA);
            return set.Contains(userB);
        }

        // Returns true if either side has blocked the other — for cases like sending a DM
        // where direction doesn't matter, only "is there an active block between us".
        public async Task<bool> EitherBlocksCachedAsync(Guid userA, Guid userB)
        {
            var outSet = await GetBlocksOutSetAsync(userA);
            if (outSet.Contains(userB)) return true;
            var inSet = await GetBlocksInSetAsync(userA);
            return inSet.Contains(userB);
        }

        public async Task<bool> IsBlockedByCachedAsync(Guid blockerId, Guid blockedId)
        {
            var outSet = await GetBlocksOutSetAsync(blockerId);
            return outSet.Contains(blockedId);
        }

        private async Task<HashSet<Guid>> GetFriendsSetAsync(Guid userId)
        {
            string key = $"friends_set:{userId}";
            var cached = await this.cacheService.GetAsync<HashSet<Guid>>(key);
            if (cached != null) return cached;

            var ids = await this.AppUnitOfWork.FriendshipRepository.GetFriendIds(userId);
            var set = new HashSet<Guid>(ids);
            await this.cacheService.SetAsync(key, set, SocialSetTtl);
            return set;
        }

        private async Task<HashSet<Guid>> GetBlocksOutSetAsync(Guid userId)
        {
            string key = $"blocks_out:{userId}";
            var cached = await this.cacheService.GetAsync<HashSet<Guid>>(key);
            if (cached != null) return cached;

            var ids = await this.AppUnitOfWork.UserBlockRepository.GetBlockedIds(userId);
            var set = new HashSet<Guid>(ids);
            await this.cacheService.SetAsync(key, set, SocialSetTtl);
            return set;
        }

        private async Task<HashSet<Guid>> GetBlocksInSetAsync(Guid userId)
        {
            string key = $"blocks_in:{userId}";
            var cached = await this.cacheService.GetAsync<HashSet<Guid>>(key);
            if (cached != null) return cached;

            var ids = await this.AppUnitOfWork.UserBlockRepository.GetBlockerIds(userId);
            var set = new HashSet<Guid>(ids);
            await this.cacheService.SetAsync(key, set, SocialSetTtl);
            return set;
        }

        // BILATERAL invalidation helpers — every state change between two users must
        // invalidate cached sets on BOTH sides, or the other side will see stale data.

        private async Task InvalidateFriendsBoth(Guid userA, Guid userB)
        {
            await this.cacheService.RemoveAsync($"friends_set:{userA}");
            await this.cacheService.RemoveAsync($"friends_set:{userB}");
        }

        private async Task InvalidateBlocksBoth(Guid blocker, Guid blocked)
        {
            await this.cacheService.RemoveAsync($"blocks_out:{blocker}");
            await this.cacheService.RemoveAsync($"blocks_in:{blocked}");
            // Defensive: the OTHER side's caches also reflect this pair from the inverse angle.
            // If a reverse block ever existed (e.g. both-direction blocks during simultaneous
            // mutations), clearing both halves guarantees consistency.
            await this.cacheService.RemoveAsync($"blocks_out:{blocked}");
            await this.cacheService.RemoveAsync($"blocks_in:{blocker}");
        }

        // Friend-list (incoming / outgoing / my-friends) caches. A pending request between A
        // and B appears on A's outgoing list and B's incoming list, so request-state changes
        // need to clear both perspectives. An accepted request also touches both users'
        // my-friends list.
        private async Task InvalidateFriendListsBoth(Guid userA, Guid userB)
        {
            await this.cacheService.RemoveAsync($"friends_list:{userA}");
            await this.cacheService.RemoveAsync($"friends_list:{userB}");
            await this.cacheService.RemoveAsync($"friend_requests_in:{userA}");
            await this.cacheService.RemoveAsync($"friend_requests_in:{userB}");
            await this.cacheService.RemoveAsync($"friend_requests_out:{userA}");
            await this.cacheService.RemoveAsync($"friend_requests_out:{userB}");
        }

        // ─────────────────────────────────────────────────────────────────
        // QUERIES
        // ─────────────────────────────────────────────────────────────────

        public async Task<List<FriendDto>> GetMyFriends(string? search)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            // Search variants would create unbounded cache keys — only cache the no-search case.
            if (!string.IsNullOrWhiteSpace(search))
                return await this.AppUnitOfWork.FriendshipRepository.GetFriendsOf(user.UserId, search);

            string key = $"friends_list:{user.UserId}";
            var cached = await this.cacheService.GetAsync<List<FriendDto>>(key);
            if (cached != null) return cached;

            var list = await this.AppUnitOfWork.FriendshipRepository.GetFriendsOf(user.UserId, null);
            await this.cacheService.SetAsync(key, list, TimeSpan.FromMinutes(1));
            return list;
        }

        public async Task<List<FriendRequestDto>> GetIncomingRequests(string? search)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            if (!string.IsNullOrWhiteSpace(search))
                return await this.AppUnitOfWork.FriendRequestRepository.GetIncomingPending(user.UserId, search);

            string key = $"friend_requests_in:{user.UserId}";
            var cached = await this.cacheService.GetAsync<List<FriendRequestDto>>(key);
            if (cached != null) return cached;

            var list = await this.AppUnitOfWork.FriendRequestRepository.GetIncomingPending(user.UserId, null);
            await this.cacheService.SetAsync(key, list, TimeSpan.FromMinutes(1));
            return list;
        }

        public async Task<List<FriendRequestDto>> GetOutgoingRequests(string? search)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            if (!string.IsNullOrWhiteSpace(search))
                return await this.AppUnitOfWork.FriendRequestRepository.GetOutgoingPending(user.UserId, search);

            string key = $"friend_requests_out:{user.UserId}";
            var cached = await this.cacheService.GetAsync<List<FriendRequestDto>>(key);
            if (cached != null) return cached;

            var list = await this.AppUnitOfWork.FriendRequestRepository.GetOutgoingPending(user.UserId, null);
            await this.cacheService.SetAsync(key, list, TimeSpan.FromMinutes(1));
            return list;
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

            if (await this.IsBlockedByCachedAsync(user.UserId, otherUserId))
            {
                result.Status = FriendRelationStatus.BlockedByMe;
                return result;
            }

            if (await this.IsBlockedByCachedAsync(otherUserId, user.UserId))
            {
                result.Status = FriendRelationStatus.BlockedByOther;
                return result;
            }

            if (await this.AreFriendsCachedAsync(user.UserId, otherUserId))
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
                throw new BusinessRuleException("You cannot send a friend request to yourself.");

            var target = await this.AppUnitOfWork.UserRepository.GetById(toUserId);
            if (target == null)
                throw new BusinessRuleException("User not found.");

            if (await this.AppUnitOfWork.UserBlockRepository.EitherBlocks(user.UserId, toUserId))
                throw new BusinessRuleException("Cannot send a request — there is an active block between the users.");

            if (await this.AppUnitOfWork.FriendshipRepository.AreFriends(user.UserId, toUserId))
                throw new BusinessRuleException("You are already friends.");

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

                throw new BusinessRuleException("A pending request already exists.");
            }

            var request = new FriendRequestEntity
            {
                FromUserId = user.UserId,
                ToUserId = toUserId,
                Status = FriendRequestStatus.Pending,
            };

            await this.AppUnitOfWork.FriendRequestRepository.AddEntity(request, this.UserContextReader);
            await this.SaveAsync();

            // New pending request → both users' incoming/outgoing lists are stale.
            await InvalidateFriendListsBoth(user.UserId, toUserId);

            // Recipient's incoming friend-request badge just went up.
            await this.badgeService.PushAsync(toUserId);

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
                throw new BusinessRuleException("Friend request not found.");

            if (request.ToUserId != user.UserId)
                throw new BusinessRuleException("Only the recipient can accept this request.");

            if (request.Status != FriendRequestStatus.Pending)
                throw new BusinessRuleException("This request is no longer pending.");

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

            // Friendship formed → friends_set on BOTH sides must be invalidated so the
            // new friend shows up in either direction's next relation check.
            await InvalidateFriendsBoth(request.FromUserId, request.ToUserId);
            // The request moved out of pending into accepted, and a new friend was added.
            await InvalidateFriendListsBoth(request.FromUserId, request.ToUserId);

            // The recipient's incoming friend-request badge just dropped.
            await this.badgeService.PushAsync(user.UserId);

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
                throw new BusinessRuleException("Friend request not found.");

            if (request.ToUserId != user.UserId)
                throw new BusinessRuleException("Only the recipient can reject this request.");

            if (request.Status != FriendRequestStatus.Pending)
                throw new BusinessRuleException("This request is no longer pending.");

            request.Status = FriendRequestStatus.Rejected;
            await this.AppUnitOfWork.FriendRequestRepository.UpdateEntity(request, this.UserContextReader);
            await this.SaveAsync();

            // Rejected request leaves pending lists on both sides.
            await InvalidateFriendListsBoth(request.FromUserId, request.ToUserId);

            // The recipient's incoming friend-request badge just dropped.
            await this.badgeService.PushAsync(user.UserId);
        }

        public async Task CancelRequest(Guid requestId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var request = await this.AppUnitOfWork.FriendRequestRepository.GetById(requestId);
            if (request == null)
                throw new BusinessRuleException("Friend request not found.");

            if (request.FromUserId != user.UserId)
                throw new BusinessRuleException("Only the sender can cancel this request.");

            if (request.Status != FriendRequestStatus.Pending)
                throw new BusinessRuleException("This request is no longer pending.");

            request.Status = FriendRequestStatus.Cancelled;
            await this.AppUnitOfWork.FriendRequestRepository.UpdateEntity(request, this.UserContextReader);
            await this.SaveAsync();

            // Cancelled request leaves pending lists on both sides.
            await InvalidateFriendListsBoth(request.FromUserId, request.ToUserId);

            // The recipient's incoming friend-request badge just dropped.
            await this.badgeService.PushAsync(request.ToUserId);
        }

        public async Task Unfriend(Guid otherUserId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var friendship = await this.AppUnitOfWork.FriendshipRepository.Find(user.UserId, otherUserId);
            if (friendship == null)
                throw new BusinessRuleException("You are not friends with this user.");

            await this.AppUnitOfWork.FriendshipRepository.SoftDeleteEntity(friendship, this.UserContextReader);
            await this.SaveAsync();

            // Friendship dropped → friends_set on BOTH sides is stale.
            await InvalidateFriendsBoth(user.UserId, otherUserId);
            // My-friends list on both sides loses an entry.
            await InvalidateFriendListsBoth(user.UserId, otherUserId);
        }

        public async Task Block(Guid otherUserId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            if (otherUserId == user.UserId)
                throw new BusinessRuleException("You cannot block yourself.");

            var target = await this.AppUnitOfWork.UserRepository.GetById(otherUserId);
            if (target == null)
                throw new BusinessRuleException("User not found.");

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

            // Block created (or resurrected) → blocks_out:user / blocks_in:other are stale.
            // Friendship may have been soft-deleted above → friends_set on both sides too.
            // Pending request, if any, was cancelled → invalidate request lists too.
            await InvalidateBlocksBoth(user.UserId, otherUserId);
            await InvalidateFriendsBoth(user.UserId, otherUserId);
            await InvalidateFriendListsBoth(user.UserId, otherUserId);
        }

        public async Task Unblock(Guid otherUserId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var block = await this.AppUnitOfWork.UserBlockRepository.Find(user.UserId, otherUserId);
            if (block == null)
                return;

            await this.AppUnitOfWork.UserBlockRepository.SoftDeleteEntity(block, this.UserContextReader);
            await this.SaveAsync();

            // Block removed → blocks_out:user / blocks_in:other are stale.
            await InvalidateBlocksBoth(user.UserId, otherUserId);
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
