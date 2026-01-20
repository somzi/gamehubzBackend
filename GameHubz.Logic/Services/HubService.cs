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

        public async Task<List<HubOverviewDto>> GetByUserOwner(Guid id)
        {
            var entities = await this.AppUnitOfWork.HubRepository.GetByUserId(id);

            return this.Mapper.Map<List<HubOverviewDto>>(entities);
        }

        public async Task<HubOverviewDto> GetOverviewById(Guid id)
        {
            var entity = await this.AppUnitOfWork.HubRepository.GetWithDetailsById(id);

            var hubOverviewDto = this.Mapper.Map<HubOverviewDto>(entity);

            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var isUserFollowHub = entity.UserHubs!.Select(x => x.UserId).Any(x => x == user.UserId);
            var isUserOwner = entity.UserId == user.UserId;
            hubOverviewDto.IsUserFollowHub = isUserFollowHub;
            hubOverviewDto.IsUserOwner = isUserOwner;

            return hubOverviewDto;
        }

        public async Task<TournamentPagedResponse> GetTournamentsPaged(Guid id, TournamentRequest request)
        {
            return await this.tournamentService.GetTournamentsPagedForHub(id, request);
        }

        public async Task<HubOverviewDto> UpdateDetails(HubPost request)
        {
            var hub = await this.AppUnitOfWork.HubRepository.GetByIdOrThrowIfNull(request.Id!.Value);

            hub.Name = request.Name;
            hub.Description = request.Description;

            await this.AppUnitOfWork.HubRepository.UpdateEntity(hub, this.UserContextReader);

            await this.SaveAsync();

            return this.Mapper.Map<HubOverviewDto>(hub);
        }

        protected override IRepository<HubEntity> GetRepository()
        {
            return this.AppUnitOfWork.HubRepository;
        }
    }
}