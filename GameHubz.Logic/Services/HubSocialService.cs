using FluentValidation;

namespace GameHubz.Logic.Services
{
    public class HubSocialService : AppBaseServiceGeneric<HubSocialEntity, HubSocialDto, HubSocialPost, HubSocialEdit>
    {
        private readonly ICacheService cacheService;
        private readonly UserHubService userHubService;

        public HubSocialService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<HubSocialEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader,
            ICacheService cacheService,
            UserHubService userHubService) : base(
                factory.CreateAppUnitOfWork(),
                userContextReader,
                localizationService,
                searchService,
                validator,
                mapper,
                serviceFunctions)
        {
            this.cacheService = cacheService;
            this.userHubService = userHubService;
        }

        protected override IRepository<HubSocialEntity> GetRepository()
            => this.AppUnitOfWork.HubSocialRepository;

        protected override async Task BeforeSave(HubSocialEntity entity, HubSocialPost inputDto, bool isNew)
        {
            if (inputDto.HubId.HasValue)
            {
                var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
                await this.userHubService.EnsureCallerCanManage(inputDto.HubId.Value, caller.UserId);
            }

            await cacheService.RemoveAsync($"hub_overview:{inputDto.HubId}");
        }

        protected override async Task BeforeDelete(Guid entityId)
        {
            // entityId is the HubSocial id, not the hub id — resolve the owning hub
            // so we bust the correct hub_overview cache entry, otherwise the deleted
            // social keeps showing until the cache expires.
            var social = await this.GetRepository().GetById(entityId);
            if (social?.HubId != null)
            {
                var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
                await this.userHubService.EnsureCallerCanManage(social.HubId.Value, caller.UserId);

                await cacheService.RemoveAsync($"hub_overview:{social.HubId}");
            }
        }
    }
}