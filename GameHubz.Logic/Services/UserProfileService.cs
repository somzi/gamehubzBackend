namespace GameHubz.Logic.Services
{
    public class UserProfileService : AppBaseService
    {
        private readonly IMapper mapper;

        public UserProfileService(
            IMapper mapper,
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.mapper = mapper;
        }

        public async Task<PlayerMatchesDto> GetStats(Guid id)
        {
            var statsTask = await this.AppUnitOfWork.MatchRepository.GetStatsByUserId(id);

            var matchesTask = await this.AppUnitOfWork.MatchRepository.GetLastMatchesByUserId(id);

            return new PlayerMatchesDto
            {
                Stats = statsTask,
                LastMatches = matchesTask
            };
        }

        public async Task<UserProfileDto> GetUserProfileAsync(Guid userId)
        {
            var userEntity = await this.AppUnitOfWork.UserRepository.GetWithSocials(userId);

            var userProfileDto = this.mapper.Map<UserProfileDto>(userEntity);

            return userProfileDto;
        }
    }
}