using FluentValidation;
using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class TournamentService : AppBaseServiceGeneric<TournamentEntity, TournamentDto, TournamentPost, TournamentEdit>
    {
        private readonly HubActivityService hubActivityService;
        private readonly ICacheService cacheService;

        public TournamentService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<TournamentEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader,
            HubActivityService hubActivityService,
            ICacheService cacheService) : base(
                factory.CreateAppUnitOfWork(),
                userContextReader,
                localizationService,
                searchService,
                validator,
                mapper,
                serviceFunctions)
        {
            this.hubActivityService = hubActivityService;
            this.cacheService = cacheService;
        }

        public async Task<TournamentPagedResponse> GetTournamentsPagedForHub(Guid hubId, TournamentRequest request)
        {
            string statusKey = request.Status.ToString();
            string cacheKey = $"tournaments:hub:{hubId}:status:{statusKey}:p:{request.Page}:s:{request.PageSize}";

            var cachedResponse = await cacheService.GetAsync<TournamentPagedResponse>(cacheKey);
            if (cachedResponse != null)
            {
                return cachedResponse;
            }

            var tournaments = await this.AppUnitOfWork.TournamentRepository.GetByHubPaged(hubId, request.Status, request.Page, request.PageSize);
            var tournamentsCount = await this.AppUnitOfWork.TournamentRepository.GetByHubCount(hubId, request.Status);

            var response = new TournamentPagedResponse
            {
                Count = tournamentsCount,
                Tournaments = tournaments
            };

            await cacheService.SetAsync(cacheKey, response, TimeSpan.FromSeconds(30));

            return response;
        }

        public async Task<TournamentPagedResponse> GetTournamentPagedForUser(Guid userId, UserTournamentRequest request)
        {
            string statusKey = request.Status.ToString();

            string cacheKey = $"user_feed:{userId}:st:{statusKey}:p:{request.Page}:s:{request.PageSize}";

            var cachedResponse = await cacheService.GetAsync<TournamentPagedResponse>(cacheKey);
            if (cachedResponse != null)
            {
                return cachedResponse;
            }

            List<Guid> hubIds = await this.AppUnitOfWork.HubRepository.GetHubIdsByUserId(userId);

            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var userRegion = (RegionType)user.Region!.Value;

            List<TournamentOverview> tournaments = await this.AppUnitOfWork.TournamentRepository.GetByHubsPaged(userId, hubIds, request.Status, userRegion, request.Page, request.PageSize);

            var tournamentsCount = await this.AppUnitOfWork.TournamentRepository.GetCountByHubs(userId, hubIds, userRegion, request.Status);

            var response = new TournamentPagedResponse
            {
                Count = tournamentsCount,
                Tournaments = tournaments
            };

            await cacheService.SetAsync(cacheKey, response, TimeSpan.FromSeconds(30));

            return response;
        }

        public async Task<TournamentDto> GetDetailsById(Guid id)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(id);

            return this.Mapper.Map<TournamentDto>(tournament);
        }

        public async Task CloseRegistration(Guid id)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithPendingRegistration(id);

            if (tournament.TournamentParticipants != null && tournament.TournamentParticipants.Count < 2)
            {
                throw new Exception("A tournament requires a minimum of 2 participants.");
            }

            tournament.Status = TournamentStatus.RegistrationClosed;

            await RejectPendings(tournament);

            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);

            await SaveAsync();

            await cacheService.RemoveAsync($"tournament:{id}");
            await cacheService.RemoveAsync($"bracket:{id}");
        }

        public async Task Publish(Guid id)
        {
            await OpenRegistration(id);
        }

        public async Task OpenRegistration(Guid id)
        {
            var tournament = await ChangeTournamentStatus(
                id,
                TournamentStatus.RegistrationOpen,
                ShouldOpenRegistration,
                "Tournament registration can be opened only when it is closed."
            );

            await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.RegistrationOpen);
        }

        public async Task<TournamentOverview> GetOverview(Guid id)
        {
            string cacheKey = $"tournament:{id}";

            var cachedTournament = await cacheService.GetAsync<TournamentOverview>(cacheKey);
            if (cachedTournament != null)
            {
                return cachedTournament;
            }
            var data = await this.AppUnitOfWork.TournamentRepository.GetOverview(id);

            await cacheService.SetAsync(cacheKey, data, TimeSpan.FromMinutes(1));

            return data!;
        }

        private async Task RejectPendings(TournamentEntity tournament)
        {
            foreach (var registration in tournament.TournamentRegistrations!)
            {
                registration.Status = TournamentRegistrationStatus.Rejected;
                await this.AppUnitOfWork.TournamentRegistrationRepository.UpdateEntity(registration, this.UserContextReader);
            }
        }

        protected override IRepository<TournamentEntity> GetRepository()
            => this.AppUnitOfWork.TournamentRepository;

        public async Task<bool> CheckIsUserRegistred(Guid id, Guid userId)
        {
            var isUserAlreadyRegistred = await this.AppUnitOfWork.TournamentRepository.CheckIsUserIsRegistered(id, userId);

            return isUserAlreadyRegistred;
        }

        public async Task SetRoundDeadline(Guid tournamentId, int roundNumber, DateTime? deadline, DateTime? roundStart)
        {
            if (roundNumber < 1)
                throw new Exception("Round number must be greater than 0.");

            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithHubById(tournamentId);

            if (tournament.Hub!.UserId != currentUser.UserId)
                throw new Exception("Only tournament admin can manage round deadlines.");

            var roundMatches = await this.AppUnitOfWork.MatchRepository.GetByTournamentAndRound(tournamentId, roundNumber);
            if (roundMatches.Count == 0)
                throw new Exception("Round not found.");

            foreach (var match in roundMatches)
            {
                if (roundStart != null) match.RoundOpenAt = roundStart;
                if (deadline != null) match.RoundDeadline = deadline;
                await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            }

            await this.SaveAsync();
            await cacheService.RemoveAsync($"bracket:{tournamentId}");
            await cacheService.RemoveAsync($"tournament:{tournamentId}");
        }

        public async Task CancelTournament(Guid id)
        {
            var tournament = await ChangeTournamentStatus(
                id,
                TournamentStatus.Cancelled,
                ShouldCancelTournament,
                "Tournament can be cancelled only when it is in progress."
            );

            await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentCanceled);
        }

        public async Task HardDeleteTournament(Guid id)
        {
            var tournament = await ChangeTournamentStatus(
                id,
                TournamentStatus.Deleted,
                ShouldDeleteTournament,
                "Tournament can be deleted only when it is in progress or completed."
            );

            await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentDeleted);
        }

        private async Task<TournamentEntity> ChangeTournamentStatus(
            Guid id,
            TournamentStatus newStatus,
            Func<TournamentEntity, bool> validator,
            string errorMessage)
        {
            var tournament = await GetHubOwnedTournamentOrThrow(id);

            if (!validator(tournament))
                throw new Exception(errorMessage);

            tournament.Status = newStatus;

            await AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, UserContextReader);
            await SaveAsync();

            await InvalidateTournamentCache(id, tournament.HubId!.Value);

            return tournament;
        }

        private async Task<TournamentEntity> GetHubOwnedTournamentOrThrow(Guid tournamentId)
        {
            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithHubById(tournamentId);

            if (tournament.Hub?.UserId != currentUser.UserId)
            {
                throw new Exception("Only hub owner can manage this tournament.");
            }

            return tournament;
        }

        public override async Task<TournamentDto> SaveEntity(TournamentPost inputDto, bool doSave = true)
        {
            TournamentDto model = await this.ServiceFunctions.SaveEntity(
                this.GetRepository(),
                this,
                this.Validator,
                inputDto,
                this.GetEntityById,
                this.BeforeSave,
                this.BeforeDtoMapToEntity,
                doSave);

            if (inputDto.Id is null)
            {
                await this.hubActivityService.LogActivity(model.HubId!.Value, model.Id!.Value, HubActivityType.RegistrationOpen);
            }
            else
            {
                await cacheService.RemoveAsync($"tournament:{model.Id}");
            }

            await cacheService.RemoveAsync($"tournaments:hub:{inputDto.HubId}:status:RegistrationOpen:p:0:s:10");
            await cacheService.RemoveAsync($"hub_overview:{model.HubId!.Value}");

            return model;
        }

        private static bool ShouldDeleteTournament(TournamentEntity tournament)
        {
            return tournament.Status == TournamentStatus.RegistrationClosed || tournament.Status == TournamentStatus.RegistrationOpen;
        }

        private static bool ShouldCancelTournament(TournamentEntity tournament)
        {
            return tournament.Status == TournamentStatus.InProgress;
        }

        private static bool ShouldOpenRegistration(TournamentEntity tournament)
        {
            return tournament.Status == TournamentStatus.RegistrationClosed;
        }

        private async Task InvalidateTournamentCache(Guid tournamentId, Guid hubId)
        {
            await cacheService.RemoveAsync($"bracket:{tournamentId}");
            await cacheService.RemoveAsync($"tournament:{tournamentId}");
            await cacheService.RemoveAsync($"hub_overview:{hubId}");
            await cacheService.RemoveAsync($"tournaments:hub:{hubId}:status:{TournamentStatus.InProgress}:p:{0}:s:{10}");
            await cacheService.RemoveAsync($"tournaments:hub:{hubId}:status:{TournamentStatus.InProgress}:p:{1}:s:{10}");
            await cacheService.RemoveAsync($"tournaments:hub:{hubId}:status:{TournamentStatus.RegistrationOpen}:p:{0}:s:{10}");
            await cacheService.RemoveAsync($"tournaments:hub:{hubId}:status:{TournamentStatus.RegistrationOpen}:p:{1}:s:{10}");
            await cacheService.RemoveAsync($"tournaments:hub:{hubId}:status:{TournamentStatus.RegistrationClosed}:p:{0}:s:{10}");
            await cacheService.RemoveAsync($"tournaments:hub:{hubId}:status:{TournamentStatus.RegistrationClosed}:p:{1}:s:{10}");
        }

        private async Task UpdateRoundSchedule(Guid tournamentId, int roundNumber, DateTime? opensAt = null, DateTime? deadline = null)
        {
            if (roundNumber < 1)
                throw new Exception("Round number must be greater than 0.");

            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithHubById(tournamentId);

            if (tournament.Hub!.UserId != currentUser.UserId)
                throw new Exception("Only tournament admin can manage round deadlines.");

            var roundMatches = await this.AppUnitOfWork.MatchRepository.GetByTournamentAndRound(tournamentId, roundNumber);
            if (roundMatches.Count == 0)
                throw new Exception("Round not found.");

            foreach (var match in roundMatches)
            {
                if (opensAt != null) match.RoundOpenAt = opensAt;
                if (deadline != null) match.RoundDeadline = deadline;
                await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            }

            await this.SaveAsync();
            await cacheService.RemoveAsync($"bracket:{tournamentId}");
            await cacheService.RemoveAsync($"tournament:{tournamentId}");
        }
    }
}