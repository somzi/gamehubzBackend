using FluentValidation;
using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class UserHubService : AppBaseServiceGeneric<UserHubEntity, UserHubDto, UserHubPost, UserHubEdit>
    {
        private readonly ICacheService cacheService;

        public UserHubService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<UserHubEntity> validator,
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

        public async Task Unfollow(Guid userId, Guid hubId)
        {
            var userHub = await this.AppUnitOfWork.UserHubRepository.GetByUserAndHub(userId, hubId);

            await this.AppUnitOfWork.UserHubRepository.HardDeleteEntity(userHub);

            await this.SaveAsync();

            await this.InvalidateHubCaches(userId, hubId);
        }

        public async Task<UserHubDto> AddMember(Guid hubId, Guid userId, HubRole role)
        {
            // Owner role is bound to Hub.UserId and is assigned only when the hub is created.
            if (role == HubRole.HubOwner)
            {
                throw new Exception("Owner role cannot be assigned. The hub already has an owner.");
            }

            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            await this.EnsureCallerCanManage(hubId, caller.UserId);

            // Only the Owner can grant admin privileges.
            if (role == HubRole.HubAdmin)
            {
                await this.EnsureCallerIsOwner(hubId, caller.UserId);
            }

            var existing = await this.AppUnitOfWork.UserHubRepository.FindByUserAndHub(userId, hubId);
            if (existing != null)
            {
                throw new Exception("User is already a member of this hub.");
            }

            var entity = new UserHubEntity
            {
                UserId = userId,
                HubId = hubId,
                HubRole = role
            };

            await this.AppUnitOfWork.UserHubRepository.AddEntity(entity, this.UserContextReader);
            await this.SaveAsync();

            await this.InvalidateHubCaches(userId, hubId);

            return this.Mapper.Map<UserHubDto>(entity);
        }

        public async Task<UserHubDto> ChangeMemberRole(Guid hubId, Guid userId, HubRole newRole)
        {
            // Owner role is immutable through this endpoint.
            if (newRole == HubRole.HubOwner)
            {
                throw new Exception("Owner role cannot be assigned. The hub already has an owner.");
            }

            // Only the Owner can change roles (grant or revoke admin).
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            await this.EnsureCallerIsOwner(hubId, caller.UserId);

            var member = await this.AppUnitOfWork.UserHubRepository.FindByUserAndHub(userId, hubId)
                ?? throw new Exception("Membership not found.");

            if (member.HubRole == HubRole.HubOwner)
            {
                throw new Exception("The hub owner's role cannot be changed.");
            }

            member.HubRole = newRole;

            await this.AppUnitOfWork.UserHubRepository.UpdateEntity(member, this.UserContextReader);
            await this.SaveAsync();

            await this.InvalidateHubCaches(userId, hubId);

            return this.Mapper.Map<UserHubDto>(member);
        }

        public async Task RemoveMember(Guid hubId, Guid userId)
        {
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            await this.EnsureCallerCanManage(hubId, caller.UserId);

            var member = await this.AppUnitOfWork.UserHubRepository.FindByUserAndHub(userId, hubId)
                ?? throw new Exception("Membership not found.");

            if (member.HubRole == HubRole.HubOwner)
            {
                throw new Exception("The hub owner cannot be removed.");
            }

            await this.AppUnitOfWork.UserHubRepository.SoftDeleteEntity(member, this.UserContextReader);
            await this.SaveAsync();

            await this.InvalidateHubCaches(userId, hubId);
        }

        public async Task<List<HubBanOverview>> GetBans(Guid hubId)
        {
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            await this.EnsureCallerCanManage(hubId, caller.UserId);

            return await this.AppUnitOfWork.UserHubBanRepository.GetBansByHub(hubId);
        }

        public async Task UnbanMember(Guid hubId, Guid userId)
        {
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            await this.EnsureCallerCanManage(hubId, caller.UserId);

            var ban = await this.AppUnitOfWork.UserHubBanRepository.FindActiveBan(userId, hubId)
                ?? throw new Exception("This user is not banned from this hub.");

            await this.AppUnitOfWork.UserHubBanRepository.SoftDeleteEntity(ban, this.UserContextReader);
            await this.SaveAsync();

            await this.InvalidateHubCaches(userId, hubId);
        }

        public async Task BanMember(Guid hubId, Guid userId)
        {
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            await this.EnsureCallerCanManage(hubId, caller.UserId);

            var member = await this.AppUnitOfWork.UserHubRepository.FindByUserAndHub(userId, hubId);
            if (member != null && member.HubRole == HubRole.HubOwner)
            {
                throw new Exception("The hub owner cannot be banned.");
            }

            if (member != null)
            {
                await this.AppUnitOfWork.UserHubRepository.SoftDeleteEntity(member, this.UserContextReader);
            }

            var existingBan = await this.AppUnitOfWork.UserHubBanRepository.FindActiveBan(userId, hubId);
            if (existingBan == null)
            {
                var ban = new UserHubBanEntity
                {
                    UserId = userId,
                    HubId = hubId,
                    BannedById = caller.UserId
                };

                await this.AppUnitOfWork.UserHubBanRepository.AddEntity(ban, this.UserContextReader);
            }

            var pendingRequest = await this.AppUnitOfWork.UserHubRequestRepository.GetPendingByHubAndUser(hubId, userId);
            if (pendingRequest != null)
            {
                pendingRequest.Status = JoinRequestStatus.Rejected;
                await this.AppUnitOfWork.UserHubRequestRepository.UpdateEntity(pendingRequest, this.UserContextReader);
            }

            await this.SaveAsync();

            await this.InvalidateHubCaches(userId, hubId);
        }

        protected override async Task BeforeSave(UserHubEntity entity, UserHubPost inputDto, bool isNew)
        {
            if (isNew && inputDto.HubId.HasValue && inputDto.UserId.HasValue)
            {
                var hub = await this.AppUnitOfWork.HubRepository.GetByIdOrThrowIfNull(inputDto.HubId.Value);

                var isBanned = await this.AppUnitOfWork.UserHubBanRepository.IsBanned(inputDto.UserId.Value, inputDto.HubId.Value);
                if (isBanned)
                    throw new Exception("You are banned from this hub.");

                if (!hub.IsPublic && hub.UserId != inputDto.UserId)
                    throw new Exception("This hub is private. You need to request to join.");
            }

            await this.InvalidateHubCaches(inputDto.UserId, inputDto.HubId);
        }

        protected override IRepository<UserHubEntity> GetRepository()
            => this.AppUnitOfWork.UserHubRepository;

        public async Task EnsureCallerCanManage(Guid hubId, Guid callerUserId)
        {
            var role = await this.AppUnitOfWork.UserHubRepository.GetRole(callerUserId, hubId);

            if (role != HubRole.HubOwner && role != HubRole.HubAdmin)
            {
                throw new UnauthorizedAccessToServiceException(this.LocalizationService);
            }
        }

        public async Task EnsureCallerIsOwner(Guid hubId, Guid callerUserId)
        {
            var role = await this.AppUnitOfWork.UserHubRepository.GetRole(callerUserId, hubId);

            if (role != HubRole.HubOwner)
            {
                throw new UnauthorizedAccessToServiceException(this.LocalizationService);
            }
        }

        private async Task InvalidateHubCaches(Guid? userId, Guid? hubId)
        {
            if (userId.HasValue)
            {
                await this.cacheService.RemoveAsync($"dashboard_highlights:{userId}");
                await this.cacheService.RemoveAsync($"user_hubs_list:{userId}");
            }

            await this.cacheService.RemoveAsync("hubs_overview_all");

            if (hubId.HasValue)
            {
                await this.cacheService.RemoveAsync($"hub_overview:{hubId}");
                await this.cacheService.RemoveAsync($"hubs:{hubId}:members:v2");
            }
        }
    }
}