using FluentValidation;

namespace GameHubz.Logic.Services
{
    public class UserHubService : AppBaseServiceGeneric<UserHubEntity, UserHubDto, UserHubPost, UserHubEdit>
    {
        private readonly ICacheService cacheService;

        public UserHubService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<UserHubEntity> validator,
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

        public async Task Unfollow(Guid userId, Guid hubId)
        {
            var userHub = await this.AppUnitOfWork.UserHubRepository.GetByUserAndHub(userId, hubId);

            await this.AppUnitOfWork.UserHubRepository.HardDeleteEntity(userHub);

            await this.SaveAsync();

            await cacheService.RemoveAsync($"dashboard_highlights:{userId}");
            await cacheService.RemoveAsync($"hubs_overview_all");
            await cacheService.RemoveAsync($"user_hubs_list:{userId}");
            await cacheService.RemoveAsync($"hub_overview:{hubId}");
            await cacheService.RemoveAsync($"hubs:{hubId}:members");
        }

        protected override async Task BeforeSave(UserHubEntity entity, UserHubPost inputDto, bool isNew)
        {
            await cacheService.RemoveAsync($"dashboard_highlights:{inputDto.UserId}");
            await cacheService.RemoveAsync($"hubs_overview_all");
            await cacheService.RemoveAsync($"user_hubs_list:{inputDto.UserId}");
            await cacheService.RemoveAsync($"hub_overview:{inputDto.HubId}");
            await cacheService.RemoveAsync($"hubs:{inputDto.HubId}:members");
        }

        protected override IRepository<UserHubEntity> GetRepository()
            => this.AppUnitOfWork.UserHubRepository;
    }
}