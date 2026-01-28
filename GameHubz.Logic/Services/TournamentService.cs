using Azure;
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

            await cacheService.SetAsync(cacheKey, response, TimeSpan.FromMinutes(3));

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

            List<Guid> hubIds = await this.AppUnitOfWork.UserHubRepository.GetHubIdsByUserId(userId);

            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var userRegion = (RegionType)user.Region!.Value;

            List<TournamentOverview> tournaments = await this.AppUnitOfWork.TournamentRepository.GetByHubsPaged(userId, hubIds, request.Status, userRegion, request.Page, request.PageSize);

            var tournamentsCount = await this.AppUnitOfWork.TournamentRepository.GetCountByHubs(userId, hubIds, userRegion, request.Status);

            var response = new TournamentPagedResponse
            {
                Count = tournamentsCount,
                Tournaments = tournaments
            };

            await cacheService.SetAsync(cacheKey, response, TimeSpan.FromMinutes(3));

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

            tournament.Status = TournamentStatus.RegistrationClosed;

            await RejectPendings(tournament);

            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);

            await SaveAsync();
        }

        public async Task Publish(Guid id)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.ShallowGetByIdOrThrowIfNull(id);

            tournament.Status = TournamentStatus.RegistrationOpen;

            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);

            await SaveAsync();
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

            await cacheService.SetAsync(cacheKey, data, TimeSpan.FromMinutes(30));

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

        public override async Task<TournamentDto> SaveEntity(TournamentPost inputDto, bool doSave = true)
        {
            TournamentDto model = await this.ServiceFunctions.SaveEntity(
                this.GetRepository(),
                this,
                this.Validator,
                inputDto,
                this.GetEntityById,
                this.BeforeSave,
                this.BeforeDtoMapToEntity);

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
    }
}