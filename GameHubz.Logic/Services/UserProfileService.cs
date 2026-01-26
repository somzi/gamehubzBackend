namespace GameHubz.Logic.Services
{
    public class UserProfileService : AppBaseService
    {
        private readonly IMapper mapper;
        private readonly ICacheService cacheService;

        public UserProfileService(
            IMapper mapper,
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            ICacheService cacheService)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.mapper = mapper;
            this.cacheService = cacheService;
        }

        public async Task<PlayerMatchesDto> GetStats(Guid id)
        {
            string key = $"player_stats:{id}";

            var cachedStats = await cacheService.GetAsync<PlayerMatchesDto>(key);
            if (cachedStats != null) return cachedStats;

            var stats = await this.AppUnitOfWork.MatchRepository.GetStatsByUserId(id);
            var numberOfTournamentsWon = await this.AppUnitOfWork.TournamentRepository.GetNumberOfTournamentsWonByUserId(id);
            stats.TournamentsWon = numberOfTournamentsWon;

            var lastMatches = await this.AppUnitOfWork.MatchRepository.GetLastMatchesByUserId(id);

            var result = new PlayerMatchesDto
            {
                Stats = stats,
                LastMatches = lastMatches
            };

            await cacheService.SetAsync(key, result, TimeSpan.FromMinutes(3));

            return result;
        }

        public async Task<List<TournamentOverview>> GetTournaments(Guid id)
        {
            string key = $"player_tournaments:{id}";

            var cachedList = await cacheService.GetAsync<List<TournamentOverview>>(key);
            if (cachedList != null) return cachedList;

            var tournaments = await this.AppUnitOfWork.TournamentParticipantRepository.GetByUserId(id);

            await cacheService.SetAsync(key, tournaments, TimeSpan.FromMinutes(5));

            return tournaments;
        }

        public async Task<UserProfileDto> GetUserProfileAsync(Guid userId)
        {
            string key = $"user_profile:{userId}";

            var cachedProfile = await cacheService.GetAsync<UserProfileDto>(key);
            if (cachedProfile != null)
            {
                return cachedProfile;
            }

            var userEntity = await this.AppUnitOfWork.UserRepository.GetWithSocials(userId);

            if (userEntity == null)
                throw new Exception("User not found");

            var userProfileDto = this.mapper.Map<UserProfileDto>(userEntity);

            await cacheService.SetAsync(key, userProfileDto, TimeSpan.FromHours(1));

            return userProfileDto;
        }
    }
}