using FluentValidation;
using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class MatchService : AppBaseServiceGeneric<MatchEntity, MatchDto, MatchPost, MatchEdit>
    {
        public MatchService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<MatchEntity> validator,
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

        public async Task<List<MatchOverviewDto>> GetByUser(Guid userId)
        {
            return await this.AppUnitOfWork.MatchRepository.GetByUser(userId);
        }

        public async Task<MatchAvailabilityDto> SetAvailability(Guid matchId, List<DateTime> selectedSlots)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var userId = user.UserId;
            var match = await this.AppUnitOfWork.MatchRepository.GetById(matchId);
            if (match == null) throw new Exception("Match not found");

            // 1. Determine side (Home vs Away)
            bool isHome = match.HomeParticipant != null && match.HomeParticipant.UserId == userId;
            bool isAway = match.AwayParticipant != null && match.AwayParticipant.UserId == userId;

            if (!isHome && !isAway) throw new Exception("User is not a participant in this match");

            // 2. Save Slots
            if (isHome)
            {
                match.HomeSlots = selectedSlots;
            }
            else
            {
                match.AwaySlots = selectedSlots;
            }

            // 3. CHECK FOR OVERLAP (The Magic)
            // We check if the other person has already picked times
            var mySlots = isHome ? match.HomeSlots : match.AwaySlots;
            var opponentSlots = isHome ? match.AwaySlots : match.HomeSlots;

            // Find slots present in BOTH lists
            var intersection = mySlots.Intersect(opponentSlots).ToList();

            if (intersection.Count > 0)
            {
                // OrderBy ensures we pick the EARLIEST mutual time (e.g. 10:00 instead of 14:00)
                match.ScheduledStartTime = intersection.OrderBy(t => t).First();
                match.Status = MatchStatus.Scheduled;
            }

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await this.SaveAsync();

            // 4. Return DTO for UI
            return new MatchAvailabilityDto
            {
                MatchId = match.Id!.Value,
                MySlots = mySlots,
                OpponentSlots = opponentSlots,
                ConfirmedTime = match.ScheduledStartTime
            };
        }

        protected override IRepository<MatchEntity> GetRepository()
            => this.AppUnitOfWork.MatchRepository;
    }
}