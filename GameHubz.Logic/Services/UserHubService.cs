using FluentValidation;
using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class UserHubService : AppBaseServiceGeneric<UserHubEntity, UserHubDto, UserHubPost, UserHubEdit>
    {
        private readonly ICacheService cacheService;

        // Role lookups are read-heavy primitives (every hub view, every tournament authz check).
        // 10-minute window is safe because every write path that changes a (user, hub) role
        // explicitly invalidates the key.
        private static readonly TimeSpan UserHubRoleTtl = TimeSpan.FromMinutes(10);

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

        // Cached (user, hub) → role lookup used by HubService.GetOverviewById and
        // TournamentAuthorizationService. Returns null when the user is not a member.
        public async Task<HubRole?> GetUserHubRoleCachedAsync(Guid userId, Guid hubId)
        {
            string key = $"user_hub_role:{userId}:{hubId}";

            // Cache value encoding: 0 = not a member (sentinel — real HubRole values start at 1),
            // 1/2/3 = HubOwner/HubAdmin/HubMember. We use int? so a true cache miss (HasValue==false)
            // is distinguishable from a cached "not a member" (HasValue==true, Value==0).
            var cached = await this.cacheService.GetAsync<int?>(key);
            if (cached.HasValue)
            {
                return cached.Value == 0 ? null : (HubRole)cached.Value;
            }

            var role = await this.AppUnitOfWork.UserHubRepository.GetRole(userId, hubId);
            int toCache = role.HasValue ? (int)role.Value : 0;
            await this.cacheService.SetAsync<int?>(key, toCache, UserHubRoleTtl);
            return role;
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

            // Only the Owner can grant elevated roles (admin or exclusive).
            if (role == HubRole.HubAdmin || role == HubRole.HubExclusive)
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
            var callerRole = await this.EnsureCallerCanManage(hubId, caller.UserId);

            var member = await this.AppUnitOfWork.UserHubRepository.FindByUserAndHub(userId, hubId)
                ?? throw new Exception("Membership not found.");

            if (member.HubRole == HubRole.HubOwner)
            {
                throw new Exception("The hub owner cannot be removed.");
            }

            // Admins may only act on regular and exclusive members. Removing another
            // admin is reserved for the owner.
            if (callerRole != HubRole.HubOwner && member.HubRole == HubRole.HubAdmin)
            {
                throw new UnauthorizedAccessToServiceException(this.LocalizationService);
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
            var callerRole = await this.EnsureCallerCanManage(hubId, caller.UserId);

            var member = await this.AppUnitOfWork.UserHubRepository.FindByUserAndHub(userId, hubId);
            if (member != null && member.HubRole == HubRole.HubOwner)
            {
                throw new Exception("The hub owner cannot be banned.");
            }

            // Admins may only act on regular and exclusive members. Banning another
            // admin is reserved for the owner.
            if (callerRole != HubRole.HubOwner && member?.HubRole == HubRole.HubAdmin)
            {
                throw new UnauthorizedAccessToServiceException(this.LocalizationService);
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

        public async Task<HubRole> EnsureCallerCanManage(Guid hubId, Guid callerUserId)
        {
            var role = await this.GetUserHubRoleCachedAsync(callerUserId, hubId);

            if (role != HubRole.HubOwner && role != HubRole.HubAdmin)
            {
                throw new UnauthorizedAccessToServiceException(this.LocalizationService);
            }

            return role.Value;
        }

        public async Task EnsureCallerIsOwner(Guid hubId, Guid callerUserId)
        {
            var role = await this.GetUserHubRoleCachedAsync(callerUserId, hubId);

            if (role != HubRole.HubOwner)
            {
                throw new UnauthorizedAccessToServiceException(this.LocalizationService);
            }
        }

        // Centralised cache invalidation for every write path that changes membership/role.
        // Kept internal-ish (private) but mirrored in HubService.KickUserFromHub / Create,
        // which need the same invalidations without taking a dependency on this service.
        private async Task InvalidateHubCaches(Guid? userId, Guid? hubId)
        {
            if (userId.HasValue)
            {
                await this.cacheService.RemoveAsync($"dashboard_highlights:{userId}");
                await this.cacheService.RemoveAsync($"user_hubs_list:{userId}");

                // Joined/discovery lists are paginated — wipe every page for this user.
                await this.cacheService.RemoveByPatternAsync($"user_joined_hubs:{userId}:*");
                await this.cacheService.RemoveByPatternAsync($"user_discovery_hubs:{userId}:*");

                // Hub role changes can change tournament-management permission for the user
                // across every tournament owned by that hub. We don't know the affected
                // tournamentIds at this point, and they're cheap to recompute on next access.
                await this.cacheService.RemoveByPatternAsync($"tournament_authz:{userId}:*");
            }

            await this.cacheService.RemoveAsync("hubs_overview_all");

            if (hubId.HasValue)
            {
                await this.cacheService.RemoveAsync($"hub_overview:{hubId}");
                await this.cacheService.RemoveAsync($"hubs:{hubId}:members:v2");
            }

            if (userId.HasValue && hubId.HasValue)
            {
                await this.cacheService.RemoveAsync($"user_hub_role:{userId}:{hubId}");
            }
        }
    }
}