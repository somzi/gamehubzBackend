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

            var hubTask = GetCachedHubData(id);

            var isFollowingTask = IsUserFollowingCached(user.UserId, id);

            await Task.WhenAll(hubTask, isFollowingTask);

            var hubDto = hubTask.Result;
            var isFollowing = isFollowingTask.Result;

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

                userHubs = new HashSet<Guid>(hubIdsList);

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

        protected override IRepository<HubEntity> GetRepository()
        {
            return this.AppUnitOfWork.HubRepository;
        }
    }
}