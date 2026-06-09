using Microsoft.AspNetCore.Http;

namespace GameHubz.Logic.Services
{
    public class UserProfileService : AppBaseService
    {
        private readonly IMapper mapper;
        private readonly ICacheService cacheService;
        private readonly CloudinaryStorageService storageService;

        public UserProfileService(
            IMapper mapper,
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            ICacheService cacheService,
            CloudinaryStorageService storageService)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.mapper = mapper;
            this.cacheService = cacheService;
            this.storageService = storageService;
        }

        public async Task<List<MatchListItemDto>> GetMatches(Guid id, int pageNumber)
        {
            const int pageSize = 10;

            // Short TTL — match results change but only when the user finishes a match.
            // 30s lag is fine, no explicit invalidation needed.
            string key = $"user_matches:{id}:p:{pageNumber}";
            var cached = await cacheService.GetAsync<List<MatchListItemDto>>(key);
            if (cached != null) return cached;

            var matches = await this.AppUnitOfWork.MatchRepository.GetLastMatchesByUserId(id, pageSize, pageNumber);

            await cacheService.SetAsync(key, matches, TimeSpan.FromSeconds(30));
            return matches;
        }

        public async Task<PlayerMatchesDto> GetStats(Guid id)
        {
            string key = $"player_stats:{id}";

            var cachedStats = await cacheService.GetAsync<PlayerMatchesDto>(key);
            if (cachedStats != null) return cachedStats;

            var stats = await this.AppUnitOfWork.MatchRepository.GetStatsByUserId(id);
            var numberOfTournamentsWon = await this.AppUnitOfWork.TournamentRepository.GetNumberOfTournamentsWonByUserId(id);
            stats.TournamentsWon = numberOfTournamentsWon;

            var perforamance = await this.AppUnitOfWork.MatchRepository.GetPerformanceByUserId(id);

            var result = new PlayerMatchesDto
            {
                Stats = stats,
                Performance = perforamance
            };

            await cacheService.SetAsync(key, result, TimeSpan.FromSeconds(30));

            return result;
        }

        public async Task<EntityListDto<TournamentOverview>> GetTournaments(Guid id, int pageNumber)
        {
            const int pageSize = 10;

            string key = $"user_profile_tournaments:{id}:p:{pageNumber}";
            var cached = await cacheService.GetAsync<EntityListDto<TournamentOverview>>(key);
            if (cached != null) return cached;

            var tournaments = await this.AppUnitOfWork.TournamentParticipantRepository.GetByUserIdPaged(id, pageNumber, pageSize);

            await cacheService.SetAsync(key, tournaments, TimeSpan.FromMinutes(1));
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

        public async Task UploadAvatar(IFormFile file)
        {
            var user = await this.AppUnitOfWork.UserRepository.GetByIdOrThrowIfNull(
                (await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull()).UserId);

            string fileName = $"avatar";
            string folderPath = $"users/{user!.Username}";

            string url = await storageService.UploadFileAsync(file, folderPath, fileName);

            user.AvatarUrl = url;

            await this.AppUnitOfWork.UserRepository.UpdateEntity(user, this.UserContextReader);

            await this.SaveAsync();

            await cacheService.RemoveAsync($"user_profile:{user.Id}");
        }

        public async Task<PlayerMatchesV2Dto> GetStatsV2(Guid id)
        {
            string key = $"player_stats_v2:{id}";

            var cachedStats = await cacheService.GetAsync<PlayerMatchesV2Dto>(key);
            if (cachedStats != null) return cachedStats;

            var stats = await this.AppUnitOfWork.MatchRepository.GetStatsByUserId(id);
            var numberOfTournamentsWon = await this.AppUnitOfWork.TournamentRepository.GetNumberOfTournamentsWonByUserId(id);
            stats.TournamentsWon = numberOfTournamentsWon;

            var performance = await this.AppUnitOfWork.MatchRepository.GetPerformanceByUserIdV2(id);

            var result = new PlayerMatchesV2Dto
            {
                Stats = stats,
                Performance = performance
            };

            await cacheService.SetAsync(key, result, TimeSpan.FromSeconds(30));

            return result;
        }
    }
}