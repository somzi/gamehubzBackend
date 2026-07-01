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
            // F105: the generic save trusted a client-supplied UserId and could overwrite another
            // user's social row by Id. Bind the row to the caller and, on edit, verify the existing
            // row already belongs to them.
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            if (!isNew && inputDto.Id.HasValue)
            {
                var existing = await this.AppUnitOfWork.UserSocialRepository.GetById(inputDto.Id.Value);
                if (existing == null || existing.UserId != caller.UserId)
                {
                    throw new UnauthorizedAccessToServiceException(this.LocalizationService);
                }
            }

            entity.UserId = caller.UserId;

            await cacheService.RemoveAsync($"user_profile:{caller.UserId}");
        }

        protected override async Task BeforeDelete(Guid entityId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            // F105: only the owner may delete their own social row.
            var existing = await this.AppUnitOfWork.UserSocialRepository.GetById(entityId);
            if (existing == null || existing.UserId != user.UserId)
            {
                throw new UnauthorizedAccessToServiceException(this.LocalizationService);
            }

            await cacheService.RemoveAsync($"user_profile:{user.UserId}");
        }
    }
}