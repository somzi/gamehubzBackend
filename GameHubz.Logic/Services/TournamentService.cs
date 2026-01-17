using FluentValidation;

namespace GameHubz.Logic.Services
{
    public class TournamentService : AppBaseServiceGeneric<TournamentEntity, TournamentDto, TournamentPost, TournamentEdit>
    {
        public TournamentService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<TournamentEntity> validator,
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

        public async Task<TournamentPagedResponse> GetTournamentsPagedForHub(Guid hubId, TournamentRequest request)
        {
            var tournaments = await this.AppUnitOfWork.TournamentRepository.GetByHubPaged(hubId, request.Status, request.Page, request.PageSize);

            var tournamentsCount = await this.AppUnitOfWork.TournamentRepository.GetByHubCount(hubId, request.Status);

            return new TournamentPagedResponse
            {
                Count = tournamentsCount,
                Tournaments = tournaments
            };
        }

        public async Task<TournamentPagedResponse> GetTournamentPagedForUser(Guid userId, UserTournamentRequest request)
        {
            List<Guid> hubIds = await this.AppUnitOfWork.UserHubRepository.GetHubIdsByUserId(userId);

            List<TournamentOverview> tournaments = await this.AppUnitOfWork.TournamentRepository.GetByHubsPaged(userId, hubIds, request.Status, request.Page, request.PageSize);

            var tournamentsCount = await this.AppUnitOfWork.TournamentRepository.GetCountByHubs(userId, hubIds, request.Status);

            return new TournamentPagedResponse
            {
                Count = tournamentsCount,
                Tournaments = tournaments
            };
        }

        public async Task<TournamentDto> GetDetailsById(Guid id)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(id);

            return this.Mapper.Map<TournamentDto>(tournament);
        }

        protected override IRepository<TournamentEntity> GetRepository()
            => this.AppUnitOfWork.TournamentRepository;
    }
}