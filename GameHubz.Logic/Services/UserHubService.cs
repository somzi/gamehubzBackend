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

        protected override IRepository<UserHubEntity> GetRepository()
            => this.AppUnitOfWork.UserHubRepository;
    }
}