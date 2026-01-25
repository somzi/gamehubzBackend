using FluentValidation;
using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class HubActivityService : AppBaseServiceGeneric<HubActivityEntity, HubActivityDto, HubActivityPost, HubActivityEdit>
    {
        public HubActivityService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<HubActivityEntity> validator,
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

            var userHubs = await this.AppUnitOfWork.UserHubRepository.GetHubIdsByUserId(userId);

            var activities = await this.AppUnitOfWork.HubActivityRepository.GetRecentActivity(userHubs, 10);

            foreach (var activity in activities)
            {
                activity.TimeAgo = GetTimeAgo(activity.CreatedOn);
                activity.Message = GetMessageForType(activity.Type);
            }

            return activities;
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