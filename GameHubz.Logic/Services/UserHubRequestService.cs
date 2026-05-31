using FluentValidation;
using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class UserHubRequestService : AppBaseServiceGeneric<UserHubRequestEntity, UserHubRequestDto, UserHubRequestPost, UserHubRequestEdit>
    {
        private readonly ICacheService cacheService;

        public UserHubRequestService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<UserHubRequestEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader,
            ICacheService cacheService) : base(
                factory.CreateAppUnitOfWork(),
                userContextReader,
                localizationService,
                searchService,
                validator,
                mapper,
                serviceFunctions)
        {
            this.cacheService = cacheService;
        }

        public async Task RequestJoin(Guid hubId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var hub = await this.AppUnitOfWork.HubRepository.GetByIdOrThrowIfNull(hubId);

            if (hub.UserId == user.UserId)
                throw new Exception("You already own this hub.");

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
        }

        public async Task<List<UserHubRequestDto>> GetPendingRequests(Guid hubId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var hub = await this.AppUnitOfWork.HubRepository.GetByIdOrThrowIfNull(hubId);

            if (hub.UserId != user.UserId)
                throw new Exception("Only the hub owner can view join requests.");

            return await this.AppUnitOfWork.UserHubRequestRepository.GetPendingRequestsByHubId(hubId);
        }

        public async Task ApproveRequest(Guid requestId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var request = await this.AppUnitOfWork.UserHubRequestRepository.GetByIdWithHub(requestId);
            if (request == null) throw new Exception("Request not found.");
            if (request.Hub == null) throw new Exception("Hub not found.");

            if (request.Hub.UserId != user.UserId)
                throw new Exception("Only the hub owner can approve requests.");

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
        }

        public async Task RejectRequest(Guid requestId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var request = await this.AppUnitOfWork.UserHubRequestRepository.GetByIdWithHub(requestId);
            if (request == null) throw new Exception("Request not found.");
            if (request.Hub == null) throw new Exception("Hub not found.");

            if (request.Hub.UserId != user.UserId)
                throw new Exception("Only the hub owner can reject requests.");

            if (request.Status != JoinRequestStatus.Pending)
                throw new Exception("Request is no longer pending.");

            request.Status = JoinRequestStatus.Rejected;
            await this.AppUnitOfWork.UserHubRequestRepository.UpdateEntity(request, this.UserContextReader);

            await this.SaveAsync();
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
            await cacheService.RemoveAsync($"hubs:{hubId}:members");
        }
    }
}
