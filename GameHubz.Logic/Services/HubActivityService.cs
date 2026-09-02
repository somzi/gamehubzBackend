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
                var userHubs = await this.AppUnitOfWork.HubRepository.GetHubIdsByUserId(userId);

                if (userHubs != null && userHubs.Any())
                {
                    activities = await this.AppUnitOfWork.HubActivityRepository.GetRecentActivity(userHubs, 3);
                }
                else
                {
                    activities = new List<DashboardActivityDto>();
                }

                // Short TTL because LogActivity (called from many write paths) doesn't and
                // can't cheaply invalidate every hub-follower's dashboard cache — keep the
                // staleness window small instead of paying a fan-out cost on every activity.
                await cacheService.SetAsync(cacheKey, activities, TimeSpan.FromSeconds(30));
            }

            foreach (var activity in activities)
            {
                activity.TimeAgo = this.GetTimeAgo(activity.CreatedOn);
                activity.Message = this.GetMessageForType(activity.Type);
            }

            return activities;
        }

        public async Task<EntityListDto<DashboardActivityDto>> GetAllDashboardHighlights(int pageNumber)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var userId = user.UserId;

            var userHubs = await this.AppUnitOfWork.HubRepository.GetHubIdsByUserId(userId);
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

        private string GetMessageForType(HubActivityType type)
        {
            string key = type switch
            {
                HubActivityType.TournamentAnnounced => "HubActivity.TournamentAnnounced",
                HubActivityType.RegistrationOpen => "HubActivity.RegistrationOpen",
                HubActivityType.TournamentCanceled => "HubActivity.TournamentCanceled",
                HubActivityType.TournamentLive => "HubActivity.TournamentLive",
                HubActivityType.TournamentCompleted => "HubActivity.TournamentCompleted",
                HubActivityType.TournamentDeleted => "HubActivity.TournamentDeleted",
                _ => "HubActivity.Updated"
            };

            return this.LocalizationService[key];
        }

        private string GetTimeAgo(DateTime date)
        {
            var span = DateTime.UtcNow - date;

            if (span.TotalHours < 1)
                return string.Format(this.LocalizationService["HubActivity.MinutesAgo"], span.Minutes);
            if (span.TotalHours < 24)
                return string.Format(this.LocalizationService["HubActivity.HoursAgo"], (int)span.TotalHours);

            return string.Format(this.LocalizationService["HubActivity.DaysAgo"], (int)span.TotalDays);
        }
    }
}