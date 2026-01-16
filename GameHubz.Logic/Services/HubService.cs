using FluentValidation;

namespace GameHubz.Logic.Services
{
    public class HubService : AppBaseServiceGeneric<HubEntity, HubDto, HubPost, HubEdit>
    {
        public HubService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            IValidator<HubEntity> validator,
            ILocalizationService localizationService,
            SearchService searchService,
            IUserContextReader userContextReader,
            ServiceFunctions serviceFunctions)
            : base(
                  factory.CreateAppUnitOfWork(),
                  userContextReader,
                  localizationService,
                  searchService,
                  validator,
                  mapper,
                  serviceFunctions)
        {
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
            var tournaments = await this.AppUnitOfWork.TournamentRepository.GetByHubPaged(id, request.Status, request.Page, request.PageSize);

            var tournamentsCount = await this.AppUnitOfWork.TournamentRepository.GetByHubCount(id, request.Status);

            var tournamentDtos = this.Mapper.Map<List<TournamentDto>>(tournaments);

            return new TournamentPagedResponse
            {
                Count = tournamentsCount,
                Tournaments = tournamentDtos
            };
        }

        protected override IRepository<HubEntity> GetRepository()
        {
            return this.AppUnitOfWork.HubRepository;
        }
    }
}