using FluentValidation;

namespace GameHubz.Logic.Services
{
    public class HubService : AppBaseServiceGeneric<HubEntity, HubDto, HubPost, HubEdit>
    {
        private readonly TournamentService tournamentService;

        public HubService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            IValidator<HubEntity> validator,
            ILocalizationService localizationService,
            SearchService searchService,
            IUserContextReader userContextReader,
            ServiceFunctions serviceFunctions,
            TournamentService tournamentService)
            : base(
                  factory.CreateAppUnitOfWork(),
                  userContextReader,
                  localizationService,
                  searchService,
                  validator,
                  mapper,
                  serviceFunctions)
        {
            this.tournamentService = tournamentService;
        }

        public async Task<List<HubDto>> GetAll()
        {
            var entities = await this.AppUnitOfWork.HubRepository.GetOverview();

            return this.Mapper.Map<List<HubDto>>(entities);
        }

        public async Task<HubOverviewDto> GetOverviewById(Guid id)
        {
            var entity = await this.AppUnitOfWork.HubRepository.GetWithDetailsById(id);

            return this.Mapper.Map<HubOverviewDto>(entity);
        }

        public async Task<TournamentPagedResponse> GetTournamentsPaged(Guid id, TournamentRequest request)
        {
            return await this.tournamentService.GetTournamentsPagedForHub(id, request);
        }

        protected override IRepository<HubEntity> GetRepository()
        {
            return this.AppUnitOfWork.HubRepository;
        }
    }
}