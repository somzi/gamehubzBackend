using FluentValidation;
using GameHubz.DataModels.Enums;
using Microsoft.Extensions.Logging;

namespace GameHubz.Logic.Services
{
    public class UserHubRequestService : AppBaseServiceGeneric<UserHubRequestEntity, UserHubRequestDto, UserHubRequestPost, UserHubRequestEdit>
    {
        private readonly ICacheService cacheService;
        private readonly UserHubService userHubService;
        private readonly INotificationService notificationService;
        private readonly BadgeService badgeService;
        private readonly ILogger<UserHubRequestService> logger;

        public UserHubRequestService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<UserHubRequestEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader,
            ICacheService cacheService,
            UserHubService userHubService,
            INotificationService notificationService,
            BadgeService badgeService,
            ILogger<UserHubRequestService> logger) : base(
                factory.CreateAppUnitOfWork(),
                userContextReader,
                localizationService,
                searchService,
                validator,
                mapper,
                serviceFunctions)
        {
            this.cacheService = cacheService;
            this.userHubService = userHubService;
            this.notificationService = notificationService;
            this.badgeService = badgeService;
            this.logger = logger;
        }

        /// <summary>
        /// Taking part in a tournament follows the hub that runs it. Every entry route (solo sign-up,
        /// team registration, creating, joining or asking to join a team) is the player's own action, so
        /// this is exactly the Follow button on the hub page (<see cref="RequestJoin"/>): a public hub
        /// makes them a member, a private one files a join request for its managers.
        ///
        /// Only a first contact with the hub is followed. A player with any history there keeps its
        /// outcome: one who unfollowed (a soft delete, so the row stays), was removed or banned, or
        /// already sent the hub a join request (still waiting, approved, or turned down). Unfollows
        /// from before unfollowing was a soft delete left no trace in a public hub, so those players
        /// are followed again.
        ///
        /// Called only after the entry is saved, and never fails it. RequestJoin's refusals (owner,
        /// already a member, banned) all mean "nothing to do", a tournament with no hub or a deleted
        /// one has nothing to follow, and anything unexpected is logged and dropped.
        /// </summary>
        public async Task FollowHubOfTournament(Guid tournamentId)
        {
            try
            {
                var ownership = await this.AppUnitOfWork.TournamentRepository.GetHubOwnership(tournamentId);
                if (ownership == null) return;

                var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
                if (await this.AppUnitOfWork.UserHubRepository.HasMembershipHistory(caller.UserId, ownership.HubId)
                    || await this.AppUnitOfWork.UserHubRequestRepository.HasAnyRequest(ownership.HubId, caller.UserId))
                {
                    return;
                }

                await RequestJoin(ownership.HubId);
            }
            catch (BusinessRuleException)
            {
                // Already in, already asked, the owner, or banned.
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Following the hub of tournament {TournamentId} after an entry failed.", tournamentId);
            }
        }

        public async Task RequestJoin(Guid hubId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var hub = await this.AppUnitOfWork.HubRepository.GetByIdOrThrowIfNull(hubId);

            if (hub.UserId == user.UserId)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.YouAlreadyOwnThisHub"]);

            var isBanned = await this.AppUnitOfWork.UserHubBanRepository.IsBanned(user.UserId, hubId);
            if (isBanned)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.YouAreBanned"]);

            var alreadyFollowing = await this.AppUnitOfWork.HubRepository.IsUserFollowingHub(user.UserId, hubId);
            if (alreadyFollowing)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.YouAlreadyHubMember"]);

            if (hub.IsPublic)
            {
                var userHub = new UserHubEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = user.UserId,
                    HubId = hubId
                };

                await this.AppUnitOfWork.UserHubRepository.AddEntity(userHub, this.UserContextReader);
                await this.SaveAsync();

                await InvalidateHubCache(hubId, user.UserId);
                return;
            }

            var alreadyRequested = await this.AppUnitOfWork.UserHubRequestRepository.HasPendingRequest(hubId, user.UserId);
            if (alreadyRequested)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.PendingRequestForHub"]);

            var request = new UserHubRequestEntity
            {
                Id = Guid.NewGuid(),
                HubId = hubId,
                UserId = user.UserId,
                Status = JoinRequestStatus.Pending
            };

            await this.AppUnitOfWork.UserHubRequestRepository.AddEntity(request, this.UserContextReader);
            await this.SaveAsync();

            // Hub managers (owner + admins) have a new join request waiting.
            await NotifyHubManagersAsync(
                hubId,
                hub.UserId,
                excludeUserId: user.UserId,
                PushText.FromLiteral(hub.Name),
                PushText.FromKey("Push.HubJoinRequest.Body", user.Username),
                new { hubId = hubId.ToString(), type = "hubJoinRequest" });
        }

        // Pushes + badge-bumps every hub manager (owner + admins) about a new pending join request.
        private async Task NotifyHubManagersAsync(Guid hubId, Guid ownerUserId, Guid excludeUserId, PushText title, PushText body, object data)
        {
            var members = await this.AppUnitOfWork.UserHubRepository.GetUsersByHub(hubId);
            var managers = members
                .Where(m => (m.HubRole == HubRole.HubOwner || m.HubRole == HubRole.HubAdmin) && m.UserId != excludeUserId)
                .ToList();

            var managerIds = managers.Select(m => m.UserId).ToHashSet();
            if (ownerUserId != excludeUserId) managerIds.Add(ownerUserId); // owner may lack a UserHub row

            foreach (var id in managerIds)
                await this.badgeService.PushAsync(id);

            // Managers without a push token stay in: they get the inbox row instead of the push.
            var recipients = managers
                .Select(m => PushRecipient.ForUser(m.UserId, m.PushToken, m.Language))
                .ToList();

            if (recipients.Count == 0) return;
            _ = Task.Run(async () =>
            {
                try { await notificationService.SendLocalizedToManyAsync(recipients, title, body, data); }
                catch { /* fire-and-forget */ }
            });
        }

        // Refreshes hub managers' "pending join requests" badge after one is approved/rejected.
        private async Task BumpHubManagerBadgesAsync(Guid hubId, Guid ownerUserId)
        {
            var members = await this.AppUnitOfWork.UserHubRepository.GetUsersByHub(hubId);
            var managerIds = members
                .Where(m => m.HubRole == HubRole.HubOwner || m.HubRole == HubRole.HubAdmin)
                .Select(m => m.UserId)
                .ToHashSet();
            managerIds.Add(ownerUserId);

            foreach (var id in managerIds)
                await this.badgeService.PushAsync(id);
        }

        // Fire-and-forget push to a single user by id.
        private async Task NotifyUserAsync(Guid userId, PushText title, PushText body, object data)
        {
            var target = await this.AppUnitOfWork.UserRepository.GetById(userId);
            if (target == null) return;

            // Sent even without a push token: the inbox row is written regardless.
            var recipient = PushRecipient.ForUser(userId, target.PushToken, target.Language);
            _ = Task.Run(async () =>
            {
                try { await notificationService.SendLocalizedToOneAsync(recipient, title, body, data); }
                catch { /* fire-and-forget */ }
            });
        }

        public async Task<List<UserHubRequestDto>> GetPendingRequests(Guid hubId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            await this.userHubService.EnsureCallerCanManage(hubId, user.UserId);

            return await this.AppUnitOfWork.UserHubRequestRepository.GetPendingRequestsByHubId(hubId);
        }

        public async Task ApproveRequest(Guid requestId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var request = await this.AppUnitOfWork.UserHubRequestRepository.GetByIdWithHub(requestId);
            if (request == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.RequestNotFound"]);
            if (request.Hub == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.HubNotFound"]);

            await this.userHubService.EnsureCallerCanManage(request.HubId!.Value, user.UserId);

            if (request.Status != JoinRequestStatus.Pending)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.RequestNoLongerPending"]);

            var alreadyFollowing = await this.AppUnitOfWork.HubRepository.IsUserFollowingHub(request.UserId!.Value, request.HubId!.Value);
            if (!alreadyFollowing)
            {
                var userHub = new UserHubEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = request.UserId,
                    HubId = request.HubId
                };

                await this.AppUnitOfWork.UserHubRepository.AddEntity(userHub, this.UserContextReader);
            }

            request.Status = JoinRequestStatus.Approved;
            await this.AppUnitOfWork.UserHubRequestRepository.UpdateEntity(request, this.UserContextReader);

            await this.SaveAsync();

            await InvalidateHubCache(request.HubId!.Value, request.UserId!.Value);

            // Managers' pending-requests badge drops; tell the user they're in.
            await BumpHubManagerBadgesAsync(request.HubId!.Value, request.Hub!.UserId);
            await NotifyUserAsync(
                request.UserId!.Value,
                PushText.FromLiteral(request.Hub!.Name),
                PushText.FromKey("Push.HubJoinApproved.Body"),
                new { hubId = request.HubId!.Value.ToString(), type = "hubJoinApproved" });
        }

        public async Task RejectRequest(Guid requestId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var request = await this.AppUnitOfWork.UserHubRequestRepository.GetByIdWithHub(requestId);
            if (request == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.RequestNotFound"]);
            if (request.Hub == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.HubNotFound"]);

            await this.userHubService.EnsureCallerCanManage(request.HubId!.Value, user.UserId);

            if (request.Status != JoinRequestStatus.Pending)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.RequestNoLongerPending"]);

            request.Status = JoinRequestStatus.Rejected;
            await this.AppUnitOfWork.UserHubRequestRepository.UpdateEntity(request, this.UserContextReader);

            await this.SaveAsync();

            // Managers' pending-requests badge drops; tell the user their request was declined.
            await BumpHubManagerBadgesAsync(request.HubId!.Value, request.Hub!.UserId);
            await NotifyUserAsync(
                request.UserId!.Value,
                PushText.FromLiteral(request.Hub!.Name),
                PushText.FromKey("Push.HubJoinRejected.Body"),
                new { hubId = request.HubId!.Value.ToString(), type = "hubJoinRejected" });
        }

        public async Task CancelMyRequest(Guid hubId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var request = await this.AppUnitOfWork.UserHubRequestRepository.GetPendingByHubAndUser(hubId, user.UserId);
            if (request == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.NoPendingRequest"]);

            await this.AppUnitOfWork.UserHubRequestRepository.HardDeleteEntity(request);
            await this.SaveAsync();
        }

        protected override IRepository<UserHubRequestEntity> GetRepository()
            => this.AppUnitOfWork.UserHubRequestRepository;

        private async Task InvalidateHubCache(Guid hubId, Guid userId)
        {
            await cacheService.RemoveAsync($"dashboard_highlights:{userId}");
            await cacheService.RemoveAsync($"hubs_overview_all");
            await cacheService.RemoveAsync($"user_hubs_list:{userId}");
            await cacheService.RemoveAsync($"hub_overview:{hubId}");
            await cacheService.RemoveAsync($"hubs:{hubId}:members:v2");

            // Same set as UserHubService.InvalidateHubCaches: without these the joined/discovery
            // lists (every page) kept the old membership for a minute and the role for ten, so a
            // refresh right after following still showed the hub as not followed.
            await cacheService.RemoveByPatternAsync($"user_joined_hubs:{userId}:*");
            await cacheService.RemoveByPatternAsync($"user_discovery_hubs:{userId}:*");
            await cacheService.RemoveAsync($"user_hub_role:{userId}:{hubId}");
        }
    }
}
