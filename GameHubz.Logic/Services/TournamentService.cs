using FluentValidation;
using GameHubz.DataModels.Catalog;
using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class TournamentService : AppBaseServiceGeneric<TournamentEntity, TournamentDto, TournamentPost, TournamentEdit>
    {
        private readonly HubActivityService hubActivityService;
        private readonly ICacheService cacheService;
        private readonly INotificationService notificationService;
        private readonly TournamentAuthorizationService tournamentAuth;

        public TournamentService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<TournamentEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader,
            HubActivityService hubActivityService,
            ICacheService cacheService,
            INotificationService notificationService,
            TournamentAuthorizationService tournamentAuth) : base(
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
            this.notificationService = notificationService;
            this.tournamentAuth = tournamentAuth;
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

            // Read region + country from the DB (not the token): selecting a country changes the
            // user's region, and the JWT's region claim can be stale until the next token refresh.
            var userEntity = await this.AppUnitOfWork.UserRepository.ShallowGetByIdOrThrowIfNull(userId);
            var userRegion = userEntity.Region;
            var userCountry = userEntity.Country;

            List<TournamentOverview> tournaments = await this.AppUnitOfWork.TournamentRepository.GetByHubsPaged(userId, hubIds, request.Status, userRegion, userCountry, request.Page, request.PageSize);

            var tournamentsCount = await this.AppUnitOfWork.TournamentRepository.GetCountByHubs(userId, hubIds, userRegion, userCountry, request.Status);

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
            await cacheService.RemoveAsync($"league_standings:{id}");
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

        /// <summary>
        /// v2 of the overview endpoint. Same payload as v1 plus <see cref="TournamentOverview.CanManage"/>
        /// so the client can surface owner-level controls to hub admins / platform admins as well.
        /// CanManage is computed per request and never cached (v1 omits it entirely).
        /// </summary>
        public async Task<TournamentOverview> GetOverviewV2(Guid id)
        {
            var data = await GetOverview(id);

            data.CanManage = await this.tournamentAuth.CanManageTournamentAsync(id);

            return data;
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

            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId))
                throw new Exception("Only the hub owner or a hub admin can manage round deadlines.");

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
            await cacheService.RemoveAsync($"league_standings:{tournamentId}");
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
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithHubById(tournamentId);

            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId))
            {
                throw new Exception("Only the hub owner or a hub admin can manage this tournament.");
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

                // Notify all hub followers about the new tournament
                SendNotification(model);
            }
            else
            {
                await cacheService.RemoveAsync($"tournament:{model.Id}");
                // Tournament-level settings (e.g. RequireResultApproval) are projected into the
                // bracket structure response, so flush the bracket cache too — otherwise the new
                // setting won't be visible until the 5-minute cache window expires.
                await cacheService.RemoveAsync($"bracket:{model.Id}");
                await cacheService.RemoveAsync($"league_standings:{model.Id}");
            }

            // Wipe every paginated tournament list cached for this hub — the new (or edited)
            // tournament could appear on any page, not just page 0.
            await cacheService.RemoveByPatternAsync($"tournaments:hub:{inputDto.HubId}:*");
            await cacheService.RemoveAsync($"hub_overview:{model.HubId!.Value}");

            return model;
        }

        /// <summary>
        /// When a tournament is created/edited with one or more countries, it becomes country-scoped
        /// and its Region is derived from the first country (country dictates region). An empty/null
        /// list leaves the tournament region-scoped using the explicitly chosen Region. Stored as
        /// canonical, de-duplicated ISO codes (null when none — never an empty array).
        /// </summary>
        protected override async Task BeforeSave(TournamentEntity entity, TournamentPost inputDto, bool isNew)
        {
            var codes = inputDto.Countries?
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c =>
                {
                    var country = CountryCatalog.Get(c)
                        ?? throw new Exception($"Unknown country code '{c}'.");
                    return country.Code;
                })
                .Distinct()
                .ToList();

            if (codes is null || codes.Count == 0)
            {
                entity.Countries = null;
            }
            else
            {
                entity.Countries = codes;
                // Region is cosmetic for country-scoped tournaments (filtering uses Countries);
                // derive it from the first country so the displayed region stays sensible.
                entity.Region = CountryCatalog.Get(codes[0])!.Region;
            }

            await Task.CompletedTask;
        }

        private void SendNotification(TournamentDto model)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var hubMembers = await this.AppUnitOfWork.UserHubRepository.GetUsersByHub(model.HubId!.Value);
                    if (hubMembers == null || hubMembers.Count == 0) return;

                    var pushTokens = hubMembers
                        .Where(m => m.HubRole != HubRole.HubOwner && !string.IsNullOrEmpty(m.PushToken))
                        .Select(m => m.PushToken)
                        .Distinct()
                        .ToList();

                    if (pushTokens.Count > 0)
                    {
                        await notificationService.SendToManyAsync(
                            pushTokens!,
                            model.Name,
                            "Registration is open, grab your spot!",
                            new { tournamentId = model.Id });
                    }
                }
                catch { /* fire-and-forget */ }
            });
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
            await cacheService.RemoveAsync($"league_standings:{tournamentId}");
            await cacheService.RemoveAsync($"tournament:{tournamentId}");
            await cacheService.RemoveAsync($"hub_overview:{hubId}");
            // Wipes every cached page of every status for this hub — replaces the old
            // p:0/p:1 hand-listed invalidation that left page 2+ stale.
            await cacheService.RemoveByPatternAsync($"tournaments:hub:{hubId}:*");
        }

        private async Task UpdateRoundSchedule(Guid tournamentId, int roundNumber, DateTime? opensAt = null, DateTime? deadline = null)
        {
            if (roundNumber < 1)
                throw new Exception("Round number must be greater than 0.");

            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId))
                throw new Exception("Only the hub owner or a hub admin can manage round deadlines.");

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
            await cacheService.RemoveAsync($"league_standings:{tournamentId}");
            await cacheService.RemoveAsync($"tournament:{tournamentId}");
        }
    }
}