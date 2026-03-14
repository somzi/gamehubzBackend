using FluentValidation;

namespace GameHubz.Logic.Services
{
    public class TournamentParticipantService : AppBaseServiceGeneric<TournamentParticipantEntity, TournamentParticipantDto, TournamentParticipantPost, TournamentParticipantEdit>
    {
        public TournamentParticipantService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<TournamentParticipantEntity> validator,
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

        protected override IRepository<TournamentParticipantEntity> GetRepository()
            => this.AppUnitOfWork.TournamentParticipantRepository;

        public async Task<List<TournamentParticipantOverview>> GetByTournament(Guid tournamentId)
        {
            var participants = await this.AppUnitOfWork.TournamentParticipantRepository.GetByTournamentId(tournamentId);

            return this.Mapper.Map<List<TournamentParticipantOverview>>(participants);
        }

        public async Task RemoveUser(Guid tournamentId, Guid userId)
        {
            var participant = await this.AppUnitOfWork.TournamentParticipantRepository.GetUserByTournamentId(tournamentId, userId);

            var registration = await this.AppUnitOfWork.TournamentRegistrationRepository.GetUserByTournamentId(tournamentId, userId);

            await this.AppUnitOfWork.TournamentParticipantRepository.HardDeleteEntity(participant);
            await this.AppUnitOfWork.TournamentRegistrationRepository.HardDeleteEntity(registration);

            await this.SaveAsync();
        }
    }
}