using FluentValidation;
using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class HubActivityService : AppBaseServiceGeneric<HubActivityEntity, HubActivityDto, HubActivityPost, HubActivityEdit>
    {
        private readonly ICacheService cacheService;

        public HubActivityService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<HubActivityEntity> validator,
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

        protected override IRepository<HubActivityEntity> GetRepository()
            => this.AppUnitOfWork.HubActivityRepository;

        public async Task LogActivity(Guid hubId, Guid tournamentId, HubActivityType type)
        {
            var activity = new HubActivityEntity
            {
                HubId = hubId,
                TournamentId = tournamentId,
                Type = type
            };

            await this.AppUnitOfWork.HubActivityRepository.AddEntity(activity, this.UserContextReader);

            await this.SaveAsync();
        }

        public async Task<List<DashboardActivityDto>> GetDashboardHighlights()
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var userId = user.UserId;

            string cacheKey = $"dashboard_highlights:{userId}";

            var activities = await cacheService.GetAsync<List<DashboardActivityDto>>(cacheKey);

            if (activities == null)
            {
                var userHubs = await this.AppUnitOfWork.UserHubRepository.GetHubIdsByUserId(userId);

                if (userHubs != null && userHubs.Any())
                {
                    activities = await this.AppUnitOfWork.HubActivityRepository.GetRecentActivity(userHubs, 3);
                }
                else
                {
                    activities = new List<DashboardActivityDto>();
                }

                await cacheService.SetAsync(cacheKey, activities, TimeSpan.FromMinutes(1));
            }

            foreach (var activity in activities)
            {
                activity.TimeAgo = GetTimeAgo(activity.CreatedOn);
                activity.Message = GetMessageForType(activity.Type);
            }

            return activities;
        }

        public async Task<EntityListDto<DashboardActivityDto>> GetAllDashboardHighlights(int pageNumber)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var userId = user.UserId;

            var userHubs = await this.AppUnitOfWork.UserHubRepository.GetHubIdsByUserId(userId);
            if (userHubs == null || !userHubs.Any())
            {
                return EntityListDto<DashboardActivityDto>.Empty;
            }

            var safePageNumber = pageNumber < 1 ? 1 : pageNumber;
            const int pageSize = 10;

            var result = await this.AppUnitOfWork.HubActivityRepository.GetRecentActivityPaged(userHubs, safePageNumber, pageSize);

            foreach (var activity in result.Items)
            {
                activity.TimeAgo = GetTimeAgo(activity.CreatedOn);
                activity.Message = GetMessageForType(activity.Type);
            }

            return result;
        }

        private static string GetMessageForType(HubActivityType type)
        {
            return type switch
            {
                HubActivityType.TournamentAnnounced => "announced a new tournament",
                HubActivityType.RegistrationOpen => "registration is now open",
                HubActivityType.TournamentLive => "started a live tournament",
                HubActivityType.TournamentCompleted => "tournament concluded",
                _ => "updated a tournament"
            };
        }

        private static string GetTimeAgo(DateTime date)
        {
            var span = DateTime.UtcNow - date;
            if (span.TotalHours < 1) return $"{span.Minutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            return $"{(int)span.TotalDays}d ago";
        }
    }
}