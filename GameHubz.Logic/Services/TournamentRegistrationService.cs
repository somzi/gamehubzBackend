using Azure.Core;
using FluentValidation;
using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class TournamentRegistrationService : AppBaseServiceGeneric<TournamentRegistrationEntity, TournamentRegistrationDto, TournamentRegistrationPost, TournamentRegistrationEdit>
    {
        private readonly TournamentParticipantService tournamentParticipantService;
        private readonly ICacheService cacheService;

        public TournamentRegistrationService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<TournamentRegistrationEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader,
            TournamentParticipantService tournamentParticipantService,
            ICacheService cacheService) : base(
                factory.CreateAppUnitOfWork(),
                userContextReader,
                localizationService,
                searchService,
                validator,
                mapper,
                serviceFunctions)
        {
            this.tournamentParticipantService = tournamentParticipantService;
            this.cacheService = cacheService;
        }

        protected override async Task BeforeDtoMapToEntity(TournamentRegistrationPost inputDto, bool isNew)
        {
            inputDto.Status = TournamentRegistrationStatus.Pending;

            await Task.CompletedTask;
        }

        public async Task ApproveRegistration(Guid registrationId)
        {
            var tournamentRegistration = await this.AppUnitOfWork.TournamentRegistrationRepository.GetWithTournament(registrationId);

            if (IsAlreadyFullTournament(tournamentRegistration, 1))
            {
                throw new Exception("Cannot approve registration. Tournament has reached maximum number of players.");
            }

            await SetRegistrationStatus(tournamentRegistration, TournamentRegistrationStatus.Approved);
            await CreateTournamentParticipant(tournamentRegistration);

            await this.SaveAsync();

            if (tournamentRegistration.UserId.HasValue)
            {
                await cacheService.RemoveAsync($"player_tournaments:{tournamentRegistration.UserId}");
                await cacheService.RemoveAsync($"user_feed:{tournamentRegistration.UserId}:st:AvailableToJoin:p:0:s:10");
                await cacheService.RemoveAsync($"user_feed:{tournamentRegistration.UserId}:st:Upcoming:p:0:s:10");
                await cacheService.RemoveAsync($"tournament:{tournamentRegistration.TournamentId}");
                await cacheService.RemoveAsync($"bracket:{tournamentRegistration.TournamentId}");
            }
        }

        public async Task ApproveRegistrations(List<Guid> registrationId)
        {
            List<TournamentRegistrationEntity> tournamentRegistration = await this.AppUnitOfWork.TournamentRegistrationRepository.GetByIds(registrationId);

            if (tournamentRegistration.Count > 0 && IsAlreadyFullTournament(tournamentRegistration.First(), tournamentRegistration.Count))
            {
                throw new Exception("Cannot approve registration. Tournament has reached maximum number of players.");
            }

            foreach (var registration in tournamentRegistration)
            {
                await SetRegistrationStatus(registration, TournamentRegistrationStatus.Approved);
                await CreateTournamentParticipant(registration);
            }

            await this.SaveAsync();

            foreach (var registration in tournamentRegistration)
            {
                if (registration.UserId.HasValue)
                {
                    await cacheService.RemoveAsync($"player_tournaments:{registration.UserId}");
                    await cacheService.RemoveAsync($"player_stats:{registration.UserId}");
                }
            }

            await cacheService.RemoveAsync($"tournament:{tournamentRegistration.First().TournamentId}");
            await cacheService.RemoveAsync($"bracket:{tournamentRegistration.First().TournamentId}");
        }

        public async Task RejectRegistration(Guid registrationId)
        {
            var tournamentRegistration = await this.AppUnitOfWork.TournamentRegistrationRepository.ShallowGetByIdOrThrowIfNull(registrationId);

            await SetRegistrationStatus(tournamentRegistration, TournamentRegistrationStatus.Rejected);

            await this.SaveAsync();

            await cacheService.RemoveAsync($"tournament:{tournamentRegistration.TournamentId}");
        }

        public async Task<List<TournamentRegistrationOverview>> GetPendingByTournamentId(Guid tournamentId)
        {
            var registrations = await this.AppUnitOfWork.TournamentRegistrationRepository.GetPendingByTournamenId(tournamentId);

            return registrations;
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

        private static bool IsAlreadyFullTournament(TournamentRegistrationEntity tournamentRegistration, int numberOfNewParticipient)
        {
            return tournamentRegistration.Tournament != null
                && tournamentRegistration.Tournament.TournamentParticipants != null
                && tournamentRegistration.Tournament!.MaxPlayers < (tournamentRegistration.Tournament.TournamentParticipants.Count + numberOfNewParticipient);
        }

        protected override IRepository<TournamentRegistrationEntity> GetRepository()
            => this.AppUnitOfWork.TournamentRegistrationRepository;
    }
}