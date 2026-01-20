using FluentValidation;

namespace GameHubz.Logic.Services
{
    public class UserHubService : AppBaseServiceGeneric<UserHubEntity, UserHubDto, UserHubPost, UserHubEdit>
    {
        public UserHubService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<UserHubEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader) : base(
                factory.CreateAppUnitOfWork(),
                userContextReader,
                localizationService,
                searchService,
                validator,
                mapper,
                serviceFunctions)
        {
        }

        public async Task Unfollow(Guid userId, Guid hubId)
        {
            var userHub = await this.AppUnitOfWork.UserHubRepository.GetByUserAndHub(userId, hubId);

            await this.AppUnitOfWork.UserHubRepository.HardDeleteEntity(userHub);

            await this.SaveAsync();
        }

        protected override IRepository<UserHubEntity> GetRepository()
            => this.AppUnitOfWork.UserHubRepository;
    }
}