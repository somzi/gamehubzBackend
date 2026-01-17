using FluentValidation;
using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class TournamentRegistrationService : AppBaseServiceGeneric<TournamentRegistrationEntity, TournamentRegistrationDto, TournamentRegistrationPost, TournamentRegistrationEdit>
    {
        private readonly TournamentParticipantService tournamentParticipantService;

        public TournamentRegistrationService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<TournamentRegistrationEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader,
            TournamentParticipantService tournamentParticipantService) : base(
                factory.CreateAppUnitOfWork(),
                userContextReader,
                localizationService,
                searchService,
                validator,
                mapper,
                serviceFunctions)
        {
            this.tournamentParticipantService = tournamentParticipantService;
        }

        protected override async Task BeforeDtoMapToEntity(TournamentRegistrationPost inputDto, bool isNew)
        {
            inputDto.Status = TournamentRegistrationStatus.Pending;

            await Task.CompletedTask;
        }

        public async Task ApproveRegistration(Guid registrationId)
        {
            var tournamentRegistration = await this.AppUnitOfWork.TournamentRegistrationRepository.ShallowGetByIdOrThrowIfNull(registrationId);

            await SetRegistrationStatus(tournamentRegistration, TournamentRegistrationStatus.Approved);

            await CreateTournamentParticipant(tournamentRegistration);

            await this.SaveAsync();
        }

        public async Task ApproveRegistrations(List<Guid> registrationId)
        {
            List<TournamentRegistrationEntity> tournamentRegistration = await this.AppUnitOfWork.TournamentRegistrationRepository.GetByIds(registrationId);

            foreach (var registration in tournamentRegistration)
            {
                await SetRegistrationStatus(registration, TournamentRegistrationStatus.Approved);
                await CreateTournamentParticipant(registration);
            }

            await this.SaveAsync();
        }

        public async Task RejectRegistration(Guid registrationId)
        {
            var tournamentRegistration = await this.AppUnitOfWork.TournamentRegistrationRepository.ShallowGetByIdOrThrowIfNull(registrationId);

            await SetRegistrationStatus(tournamentRegistration, TournamentRegistrationStatus.Rejected);

            await this.SaveAsync();
        }

        private async Task CreateTournamentParticipant(TournamentRegistrationEntity tournamentRegistration)
        {
            var tournamentParticipants = new TournamentParticipantPost
            {
                TournamentId = tournamentRegistration.TournamentId,
                UserId = tournamentRegistration.UserId
            };

            await this.tournamentParticipantService.SaveEntity(tournamentParticipants, false);
        }

        private async Task SetRegistrationStatus(TournamentRegistrationEntity tournamentRegistration, TournamentRegistrationStatus status)
        {
            tournamentRegistration.Status = status;

            await this.AppUnitOfWork.TournamentRegistrationRepository.UpdateEntity(tournamentRegistration, this.UserContextReader);
        }

        protected override IRepository<TournamentRegistrationEntity> GetRepository()
            => this.AppUnitOfWork.TournamentRegistrationRepository;
    }
}