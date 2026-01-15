using FluentValidation;

namespace GameHubz.Logic.Services
{
    public class TournamentStageService : AppBaseServiceGeneric<TournamentStageEntity, TournamentStageDto, TournamentStagePost, TournamentStageEdit>
    {
        public TournamentStageService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<TournamentStageEntity> validator,
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

        protected override IRepository<TournamentStageEntity> GetRepository()
            => this.AppUnitOfWork.TournamentStageRepository;
    }
}