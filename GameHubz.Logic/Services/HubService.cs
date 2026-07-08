using FluentValidation;
using GameHubz.DataModels.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;

namespace GameHubz.Logic.Services
{
    public class HubService : AppBaseServiceGeneric<HubEntity, HubDto, HubPost, HubEdit>
    {
        private readonly TournamentService tournamentService;
        private readonly ICacheService cacheService;
        private readonly CloudinaryStorageService storageService;
        private readonly UserHubService userHubService;

        public HubService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            IValidator<HubEntity> validator,
            ILocalizationService localizationService,
            SearchService searchService,
            IUserContextReader userContextReader,
            ServiceFunctions serviceFunctions,
            TournamentService tournamentService,
            ICacheService cacheService,
            CloudinaryStorageService storageService,
            UserHubService userHubService)
            : base(
                  factory.CreateAppUnitOfWork(),
                  userContextReader,
                  localizationService,
                  searchService,
                  validator,
                  mapper,
                  serviceFunctions)
        {
            this.tournamentService = tournamentService;
            this.cacheService = cacheService;
            this.storageService = storageService;
            this.userHubService = userHubService;
        }

        public async Task<List<HubDto>> GetAll()
        {
            string cacheKey = "hubs_overview_all";

            var cachedHubs = await cacheService.GetAsync<List<HubDto>>(cacheKey);
            if (cachedHubs != null)
            {
                return cachedHubs;
            }

            var dtos = await this.AppUnitOfWork.HubRepository.GetOverview();

            await cacheService.SetAsync(cacheKey, dtos, TimeSpan.FromHours(1));

            return dtos;
        }

        public async Task<List<HubOverviewDto>> GetByUserOwner(Guid id)
        {
            var entities = await this.AppUnitOfWork.HubRepository.GetByUserId(id);

            var dtos = this.Mapper.Map<List<HubOverviewDto>>(entities);

            // Same secrecy rule as GetOverviewById: the Discord fields auto-map from the entity,
            // but only the owner themselves may see the webhook URL.
            var caller = await this.UserContextReader.GetTokenUserInfoFromContext();
            if (caller == null || caller.UserId != id)
            {
                foreach (var dto in dtos)
                {
                    dto.DiscordWebhookUrl = null;
                    dto.DiscordNotificationSettings = null;
                }
            }

            return dtos;
        }

        public async Task<HubOverviewDto> GetOverviewById(Guid id)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var hubTask = await GetCachedHubData(id);

            var isFollowingTask = await IsUserFollowingCached(user.UserId, id);

            var hubDto = hubTask;
            var isFollowing = isFollowingTask;

            if (hubDto == null)
            {
                throw new Exception("Hub not found");
            }

            hubDto.IsUserOwner = hubDto.UserId == user.UserId;
            hubDto.IsUserFollowHub = isFollowing;

            if (hubDto.IsUserOwner)
            {
                hubDto.UserHubRole = HubRole.HubOwner;
            }
            else
            {
                var callerRole = await this.userHubService.GetUserHubRoleCachedAsync(user.UserId, id);
                hubDto.IsUserAdmin = callerRole == HubRole.HubAdmin;
                // Caller-specific — always overwrite whatever the shared per-hub cache held.
                hubDto.UserHubRole = callerRole;
            }

            if (!hubDto.IsPublic && !isFollowing && !hubDto.IsUserOwner)
            {
                hubDto.HasPendingJoinRequest = await this.AppUnitOfWork.UserHubRequestRepository.HasPendingRequest(id, user.UserId);
            }

            // The webhook URL is a secret (anyone holding it can post to the hub's Discord channel)
            // and only the owner can edit hub settings, so strip the Discord fields for everyone else.
            // Safe to mutate: the cache round-trips through JSON, every read gets a fresh instance.
            if (!hubDto.IsUserOwner)
            {
                hubDto.DiscordWebhookUrl = null;
                hubDto.DiscordNotificationSettings = null;
            }

            return hubDto;
        }

        // Empty/whitespace becomes null (integration off). A non-empty value must be a real Discord
        // webhook endpoint — without this check the server could be pointed at an arbitrary URL and
        // used as a blind HTTP POST proxy (SSRF) by the notifiers' fire-and-forget sends.
        private static string? NormalizeDiscordWebhookUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            var trimmed = url.Trim();
            bool isDiscordWebhook =
                trimmed.StartsWith("https://discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("https://discordapp.com/api/webhooks/", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("https://ptb.discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("https://canary.discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase);

            if (!isDiscordWebhook)
                throw new BusinessRuleException("The Discord webhook URL must start with https://discord.com/api/webhooks/.");

            return trimmed;
        }

        private async Task<HubOverviewDto?> GetCachedHubData(Guid hubId)
        {
            string key = $"hub_overview:{hubId}";

            var cached = await cacheService.GetAsync<HubOverviewDto>(key);
            if (cached != null) return cached;

            var hubDto = await this.AppUnitOfWork.HubRepository.GetOverviewDtoById(hubId);

            if (hubDto != null)
            {
                await cacheService.SetAsync(key, hubDto, TimeSpan.FromMinutes(1));
            }

            return hubDto;
        }

        private async Task<bool> IsUserFollowingCached(Guid userId, Guid hubId)
        {
            string key = $"user_hubs_list:{userId}";

            var userHubs = await cacheService.GetAsync<HashSet<Guid>>(key);

            if (userHubs == null)
            {
                var hubIdsList = await this.AppUnitOfWork.HubRepository.GetHubIdsByUserId(userId);

                userHubs = [.. hubIdsList];

                await cacheService.SetAsync(key, userHubs, TimeSpan.FromMinutes(2));
            }

            return userHubs.Contains(hubId);
        }

        public async Task<TournamentPagedResponse> GetTournamentsPaged(Guid id, TournamentRequest request)
        {
            return await this.tournamentService.GetTournamentsPagedForHub(id, request);
        }

        public async Task<HubOverviewDto> UpdateDetails(HubPost request)
        {
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var hub = await this.AppUnitOfWork.HubRepository.GetByIdOrThrowIfNull(request.Id!.Value);

            if (hub.UserId != caller.UserId)
                throw new Exception("Only the hub owner can edit hub details.");

            hub.Name = request.Name;
            hub.Description = request.Description;
            hub.IsPublic = request.IsPublic;

            // Null = field not sent (older clients / screens that only edit name & privacy) → keep the
            // stored value, same preservation rule as TournamentService.BeforeDtoMapToEntity. An empty
            // string is an explicit clear from the Discord form.
            if (request.DiscordWebhookUrl != null)
                hub.DiscordWebhookUrl = NormalizeDiscordWebhookUrl(request.DiscordWebhookUrl);
            if (request.DiscordNotificationSettings != null)
                hub.DiscordNotificationSettings = string.IsNullOrWhiteSpace(request.DiscordNotificationSettings)
                    ? null
                    : request.DiscordNotificationSettings;

            await this.AppUnitOfWork.HubRepository.UpdateEntity(hub, this.UserContextReader);

            await this.SaveAsync();

            await cacheService.RemoveAsync($"hub_overview:{request.Id}");
            await cacheService.RemoveAsync($"hubs_overview_all");

            return this.Mapper.Map<HubOverviewDto>(hub);
        }

        public async Task Create(HubPost request)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var alreadyOwnsHub = await this.AppUnitOfWork.HubRepository.UserOwnsAnyHub(user.UserId);
            if (alreadyOwnsHub)
                throw new BusinessRuleException("You already own a hub and cannot create another one.");

            var hub = new HubEntity
            {
                Name = request.Name,
                Description = request.Description,
                UserId = user.UserId,
                IsPublic = request.IsPublic
            };

            await this.AppUnitOfWork.HubRepository.AddEntity(hub, this.UserContextReader);

            var ownerMembership = new UserHubEntity
            {
                UserId = hub.UserId,
                HubId = hub.Id,
                HubRole = HubRole.HubOwner
            };

            await this.AppUnitOfWork.UserHubRepository.AddEntity(ownerMembership, this.UserContextReader);

            await this.SaveAsync();

            await cacheService.RemoveAsync($"hubs_overview_all");
            // The new owner's joined-hubs list now includes this hub, and the discovery list
            // (hubs the user hasn't joined) shrinks by one. Wipe both.
            await cacheService.RemoveByPatternAsync($"user_joined_hubs:{hub.UserId}:*");
            await cacheService.RemoveByPatternAsync($"user_discovery_hubs:{hub.UserId}:*");
            await cacheService.RemoveAsync($"user_hubs_list:{hub.UserId}");
        }

        protected override IRepository<HubEntity> GetRepository()
        {
            return this.AppUnitOfWork.HubRepository;
        }

        protected override async Task BeforeDelete(Guid entityId)
        {
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var hub = await this.AppUnitOfWork.HubRepository.GetByIdOrThrowIfNull(entityId);

            if (hub.UserId != caller.UserId)
                throw new Exception("Only the hub owner can delete the hub.");

            var activities = await this.AppUnitOfWork.HubActivityRepository.GetByHubId(entityId);

            foreach (var act in activities)
            {
                await this.AppUnitOfWork.HubActivityRepository.HardDeleteEntity(act);
            }

            await this.SaveAsync();
        }

        public async Task<IEnumerable<HubDto>> GetJoinedByUser(Guid userId, int pageNumber, string? search = null)
        {
            // Search variants would create unbounded cache keys — only cache no-search.
            if (!string.IsNullOrWhiteSpace(search))
                return await this.AppUnitOfWork.HubRepository.GetHubsByUserId(userId, pageNumber, true, search);

            string key = $"user_joined_hubs:{userId}:p:{pageNumber}";
            var cached = await cacheService.GetAsync<List<HubDto>>(key);
            if (cached != null) return cached;

            var data = await this.AppUnitOfWork.HubRepository.GetHubsByUserId(userId, pageNumber, true, null);
            var list = data.ToList();
            await cacheService.SetAsync(key, list, TimeSpan.FromMinutes(1));
            return list;
        }

        public async Task<IEnumerable<HubDto>> GetUserNotJoined(Guid userId, int pageNumber, string? search = null)
        {
            if (!string.IsNullOrWhiteSpace(search))
                return await this.AppUnitOfWork.HubRepository.GetHubsByUserId(userId, pageNumber, false, search);

            string key = $"user_discovery_hubs:{userId}:p:{pageNumber}";
            var cached = await cacheService.GetAsync<List<HubDto>>(key);
            if (cached != null) return cached;

            var data = await this.AppUnitOfWork.HubRepository.GetHubsByUserId(userId, pageNumber, false, null);
            var list = data.ToList();
            await cacheService.SetAsync(key, list, TimeSpan.FromMinutes(1));
            return list;
        }

        public async Task<IEnumerable<UserHubOverview>> GetMembers(Guid id)
        {
            string cacheKey = $"hubs:{id}:members:v2";

            var cachedHubs = await cacheService.GetAsync<IEnumerable<UserHubOverview>>(cacheKey);

            if (cachedHubs != null)
            {
                return cachedHubs;
            }

            var data = await this.AppUnitOfWork.UserHubRepository.GetUsersByHub(id);

            // F55: PushToken is fetched for server-side notification fan-out but must never be exposed
            // to clients in the member list. Strip it before caching/returning the client-facing DTO.
            foreach (var member in data)
            {
                member.PushToken = null;
            }

            await cacheService.SetAsync(cacheKey, data, TimeSpan.FromMinutes(10));

            return data;
        }

        public async Task<List<UserHubOverview>> GetMembersPaged(Guid id, int pageNumber, string? search)
        {
            const int pageSize = 10;
            var data = await this.AppUnitOfWork.UserHubRepository.GetUsersByHubPaged(id, pageNumber, pageSize, search);

            // F55: never expose PushToken to clients (see GetMembers).
            foreach (var member in data)
            {
                member.PushToken = null;
            }

            return data;
        }

        public async Task KickUserFromHub(Guid hubId, Guid userId)
        {
            var userAdmin = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            // F46: only the hub owner/admin may remove a member, and the owner can never be kicked.
            // Previously the caller was read but never checked, so any user could remove anyone.
            await this.userHubService.EnsureCallerCanManage(hubId, userAdmin.UserId);

            var userhub = await this.AppUnitOfWork.UserHubRepository.GetByUserAndHub(userId, hubId);

            if (userhub.HubRole == HubRole.HubOwner)
            {
                throw new BusinessRuleException("The hub owner cannot be removed.");
            }

            await this.AppUnitOfWork.UserHubRepository.SoftDeleteEntity(userhub, UserContextReader);

            await this.SaveAsync();

            await cacheService.RemoveAsync($"dashboard_highlights:{userId}");
            await cacheService.RemoveAsync($"hubs_overview_all");
            await cacheService.RemoveAsync($"user_hubs_list:{userId}");
            await cacheService.RemoveAsync($"hub_overview:{hubId}");
            await cacheService.RemoveAsync($"hubs:{hubId}:members:v2");
            await cacheService.RemoveAsync($"user_hub_role:{userId}:{hubId}");
            await cacheService.RemoveByPatternAsync($"user_joined_hubs:{userId}:*");
            await cacheService.RemoveByPatternAsync($"user_discovery_hubs:{userId}:*");
            await cacheService.RemoveByPatternAsync($"tournament_authz:{userId}:*");
        }

        public async Task UploadAvatar(Guid id, IFormFile file)
        {
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var hub = await this.AppUnitOfWork.HubRepository.GetByIdOrThrowIfNull(id);

            if (hub.UserId != caller.UserId)
                throw new Exception("Only the hub owner can change the hub avatar.");

            string fileName = $"avatar";
            string folderPath = $"hubs/{hub!.Name}";

            string url = await storageService.UploadFileAsync(file, folderPath, fileName);

            hub.AvatarUrl = url;

            await this.AppUnitOfWork.HubRepository.UpdateEntity(hub, this.UserContextReader);

            await this.SaveAsync();

            await cacheService.RemoveAsync($"hub_overview:{id}");
        }
    }
}