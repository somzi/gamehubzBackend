using FluentValidation;

namespace GameHubz.Logic.Services
{
    public class HubService : AppBaseServiceGeneric<HubEntity, HubDto, HubPost, HubEdit>
    {
        private readonly TournamentService tournamentService;
        private readonly ICacheService cacheService;

        public HubService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            IValidator<HubEntity> validator,
            ILocalizationService localizationService,
            SearchService searchService,
            IUserContextReader userContextReader,
            ServiceFunctions serviceFunctions,
            TournamentService tournamentService,
            ICacheService cacheService)
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

            return this.Mapper.Map<List<HubOverviewDto>>(entities);
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

            return hubDto;
        }

        private async Task<HubOverviewDto?> GetCachedHubData(Guid hubId)
        {
            string key = $"hub_overview:{hubId}";

            var cached = await cacheService.GetAsync<HubOverviewDto>(key);
            if (cached != null) return cached;

            var hubDto = await this.AppUnitOfWork.HubRepository.GetOverviewDtoById(hubId);

            if (hubDto != null)
            {
                await cacheService.SetAsync(key, hubDto, TimeSpan.FromMinutes(10));
            }

            return hubDto;
        }

        private async Task<bool> IsUserFollowingCached(Guid userId, Guid hubId)
        {
            string key = $"user_hubs_list:{userId}";

            var userHubs = await cacheService.GetAsync<HashSet<Guid>>(key);

            if (userHubs == null)
            {
                var hubIdsList = await this.AppUnitOfWork.UserHubRepository.GetHubIdsByUserId(userId);

                userHubs = [.. hubIdsList];

                await cacheService.SetAsync(key, userHubs, TimeSpan.FromMinutes(15));
            }

            return userHubs.Contains(hubId);
        }

        public async Task<TournamentPagedResponse> GetTournamentsPaged(Guid id, TournamentRequest request)
        {
            return await this.tournamentService.GetTournamentsPagedForHub(id, request);
        }

        public async Task<HubOverviewDto> UpdateDetails(HubPost request)
        {
            var hub = await this.AppUnitOfWork.HubRepository.GetByIdOrThrowIfNull(request.Id!.Value);

            hub.Name = request.Name;
            hub.Description = request.Description;

            await this.AppUnitOfWork.HubRepository.UpdateEntity(hub, this.UserContextReader);

            await this.SaveAsync();

            await cacheService.RemoveAsync($"hub_overview:{request.Id}");

            return this.Mapper.Map<HubOverviewDto>(hub);
        }

        public async Task Create(HubPost request)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var hub = new HubEntity
            {
                Name = request.Name,
                Description = request.Description,
                UserId = user.UserId
            };

            await this.AppUnitOfWork.HubRepository.AddEntity(hub, this.UserContextReader);

            await this.SaveAsync();

            await cacheService.RemoveAsync($"hubs_overview_all");
        }

        protected override IRepository<HubEntity> GetRepository()
        {
            return this.AppUnitOfWork.HubRepository;
        }

        public async Task<IEnumerable<HubDto>> GetJoinedByUser(Guid userId, int pageNumber)
        {
            var data = await this.AppUnitOfWork.HubRepository.GetHubsByUserId(userId, pageNumber, true);

            return data;
        }

        public async Task<IEnumerable<HubDto>> GetUserNotJoined(Guid userId, int pageNumber)
        {
            var data = await this.AppUnitOfWork.HubRepository.GetHubsByUserId(userId, pageNumber, false);

            return data;
        }

        public async Task<IEnumerable<UserHubOverview>> GetMembers(Guid id)
        {
            string cacheKey = $"hubs:{id}:members";

            var cachedHubs = await cacheService.GetAsync<IEnumerable<UserHubOverview>>(cacheKey);

            if (cachedHubs != null)
            {
                return cachedHubs;
            }

            var data = await this.AppUnitOfWork.UserHubRepository.GetUsersByHub(id);

            await cacheService.SetAsync(cacheKey, data, TimeSpan.FromMinutes(60));

            return data;
        }

        public async Task KickUserFromHub(Guid hubId, Guid userId)
        {
            var userhub = await this.AppUnitOfWork.UserHubRepository.GetByUserAndHub(userId, hubId);
            var userAdmin = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            await this.AppUnitOfWork.UserHubRepository.SoftDeleteEntity(userhub, UserContextReader);

            await this.SaveAsync();

            await cacheService.RemoveAsync($"dashboard_highlights:{userId}");
            await cacheService.RemoveAsync($"hubs_overview_all");
            await cacheService.RemoveAsync($"user_hubs_list:{userId}");
            await cacheService.RemoveAsync($"hub_overview:{hubId}");
            await cacheService.RemoveAsync($"hubs:{hubId}:members");
        }
    }
}