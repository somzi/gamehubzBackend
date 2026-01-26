using FluentValidation;

namespace GameHubz.Logic.Services
{
    public class UserSocialService : AppBaseServiceGeneric<UserSocialEntity, UserSocialDto, UserSocialPost, UserSocialEdit>
    {
        private readonly ICacheService cacheService;

        public UserSocialService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<UserSocialEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader,
            ICacheService cacheService) : base(
                factory.CreateAppUnitOfWork(),
                userContextReader,
                localizationService,
                searchService,
                validator,
                mapper,
                serviceFunctions)
        {
            this.cacheService = cacheService;
        }

        protected override IRepository<UserSocialEntity> GetRepository()
            => this.AppUnitOfWork.UserSocialRepository;

        protected override async Task BeforeSave(UserSocialEntity entity, UserSocialPost inputDto, bool isNew)
        {
            await cacheService.RemoveAsync($"user_profile:{inputDto.UserId}");
        }
    }
}