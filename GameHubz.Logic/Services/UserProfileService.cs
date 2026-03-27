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
            var matches = await this.AppUnitOfWork.MatchRepository.GetLastMatchesByUserId(id, pageSize, pageNumber);

            return matches;
        }

        public async Task<PlayerMatchesDto> GetStats(Guid id)
        {
            string key = $"player_stats:{id}";

            var cachedStats = await cacheService.GetAsync<PlayerMatchesDto>(key);
            if (cachedStats != null) return cachedStats;

            var statsTask = this.AppUnitOfWork.MatchRepository.GetStatsByUserId(id);
            var performanceTask = this.AppUnitOfWork.MatchRepository.GetPerformanceByUserId(id);
            var tournamentsWonTask = this.AppUnitOfWork.TournamentRepository.GetNumberOfTournamentsWonByUserId(id);

            await Task.WhenAll(statsTask, performanceTask, tournamentsWonTask);

            var stats = statsTask.Result;
            var performance = performanceTask.Result;
            var tournamentsWon = tournamentsWonTask.Result;

            stats.TournamentsWon = tournamentsWon;

            var result = new PlayerMatchesDto
            {
                Stats = stats,
                Performance = performance
            };

            await cacheService.SetAsync(key, result, TimeSpan.FromMinutes(3));

            return result;
        }

        public async Task<EntityListDto<TournamentOverview>> GetTournaments(Guid id, int pageNumber)
        {
            const int pageSize = 10;
            var tournaments = await this.AppUnitOfWork.TournamentParticipantRepository.GetByUserIdPaged(id, pageNumber, pageSize);

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
    }
}