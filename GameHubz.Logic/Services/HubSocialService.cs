using FluentValidation;

namespace GameHubz.Logic.Services
{
    public class HubSocialService : AppBaseServiceGeneric<HubSocialEntity, HubSocialDto, HubSocialPost, HubSocialEdit>
    {
        private readonly ICacheService cacheService;

        public HubSocialService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<HubSocialEntity> validator,
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

        protected override IRepository<HubSocialEntity> GetRepository()
            => this.AppUnitOfWork.HubSocialRepository;

        protected override async Task BeforeSave(HubSocialEntity entity, HubSocialPost inputDto, bool isNew)
        {
            await cacheService.RemoveAsync($"hub_overview:{inputDto.HubId}");
        }

        protected override async Task BeforeDelete(Guid entityId)
        {
            await cacheService.RemoveAsync($"hub_overview:{entityId}");
        }
    }
}