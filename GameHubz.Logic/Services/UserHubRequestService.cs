using FluentValidation;
using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class UserHubRequestService : AppBaseServiceGeneric<UserHubRequestEntity, UserHubRequestDto, UserHubRequestPost, UserHubRequestEdit>
    {
        private readonly ICacheService cacheService;
        private readonly UserHubService userHubService;
        private readonly INotificationService notificationService;
        private readonly BadgeService badgeService;

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
            BadgeService badgeService) : base(
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
        }

        public async Task RequestJoin(Guid hubId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var hub = await this.AppUnitOfWork.HubRepository.GetByIdOrThrowIfNull(hubId);

            if (hub.UserId == user.UserId)
                throw new Exception("You already own this hub.");

            var isBanned = await this.AppUnitOfWork.UserHubBanRepository.IsBanned(user.UserId, hubId);
            if (isBanned)
                throw new Exception("You are banned from this hub.");

            var alreadyFollowing = await this.AppUnitOfWork.HubRepository.IsUserFollowingHub(user.UserId, hubId);
            if (alreadyFollowing)
                throw new Exception("You are already a member of this hub.");

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
                throw new Exception("You already have a pending request for this hub.");

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
                hub.Name,
                $"{user.Username} wants to join your hub.",
                new { hubId = hubId.ToString(), type = "hubJoinRequest" });
        }

        // Pushes + badge-bumps every hub manager (owner + admins) about a new pending join request.
        private async Task NotifyHubManagersAsync(Guid hubId, Guid ownerUserId, Guid excludeUserId, string title, string body, object data)
        {
            var members = await this.AppUnitOfWork.UserHubRepository.GetUsersByHub(hubId);
            var managers = members
                .Where(m => (m.HubRole == HubRole.HubOwner || m.HubRole == HubRole.HubAdmin) && m.UserId != excludeUserId)
                .ToList();

            var managerIds = managers.Select(m => m.UserId).ToHashSet();
            if (ownerUserId != excludeUserId) managerIds.Add(ownerUserId); // owner may lack a UserHub row

            foreach (var id in managerIds)
                await this.badgeService.PushAsync(id);

            var tokens = managers
                .Where(m => !string.IsNullOrEmpty(m.PushToken))
                .Select(m => m.PushToken!)
                .Distinct()
                .ToList();

            if (tokens.Count == 0) return;
            _ = Task.Run(async () =>
            {
                try { await notificationService.SendToManyAsync(tokens, title, body, data); }
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
        private async Task NotifyUserAsync(Guid userId, string title, string body, object data)
        {
            var target = await this.AppUnitOfWork.UserRepository.GetById(userId);
            if (string.IsNullOrEmpty(target?.PushToken)) return;

            var token = target.PushToken!;
            _ = Task.Run(async () =>
            {
                try { await notificationService.SendToOneAsync(token, title, body, data); }
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
            if (request == null) throw new Exception("Request not found.");
            if (request.Hub == null) throw new Exception("Hub not found.");

            await this.userHubService.EnsureCallerCanManage(request.HubId!.Value, user.UserId);

            if (request.Status != JoinRequestStatus.Pending)
                throw new Exception("Request is no longer pending.");

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
                request.Hub!.Name,
                "Your request to join the hub was approved.",
                new { hubId = request.HubId!.Value.ToString(), type = "hubJoinApproved" });
        }

        public async Task RejectRequest(Guid requestId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var request = await this.AppUnitOfWork.UserHubRequestRepository.GetByIdWithHub(requestId);
            if (request == null) throw new Exception("Request not found.");
            if (request.Hub == null) throw new Exception("Hub not found.");

            await this.userHubService.EnsureCallerCanManage(request.HubId!.Value, user.UserId);

            if (request.Status != JoinRequestStatus.Pending)
                throw new Exception("Request is no longer pending.");

            request.Status = JoinRequestStatus.Rejected;
            await this.AppUnitOfWork.UserHubRequestRepository.UpdateEntity(request, this.UserContextReader);

            await this.SaveAsync();

            // Managers' pending-requests badge drops; tell the user their request was declined.
            await BumpHubManagerBadgesAsync(request.HubId!.Value, request.Hub!.UserId);
            await NotifyUserAsync(
                request.UserId!.Value,
                request.Hub!.Name,
                "Your request to join the hub was declined.",
                new { hubId = request.HubId!.Value.ToString(), type = "hubJoinRejected" });
        }

        public async Task CancelMyRequest(Guid hubId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var request = await this.AppUnitOfWork.UserHubRequestRepository.GetPendingByHubAndUser(hubId, user.UserId);
            if (request == null) throw new Exception("No pending request found.");

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
        }
    }
}
