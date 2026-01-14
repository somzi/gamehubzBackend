using FluentValidation;

namespace GameHubz.Logic.Services
{
    public class UserSocialService : AppBaseServiceGeneric<UserSocialEntity, UserSocialDto, UserSocialPost, UserSocialEdit>
    {
        public UserSocialService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<UserSocialEntity> validator,
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

        protected override IRepository<UserSocialEntity> GetRepository()
            => this.AppUnitOfWork.UserSocialRepository;
    }
}