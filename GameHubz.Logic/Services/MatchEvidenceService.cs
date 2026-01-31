using FluentValidation;

namespace GameHubz.Logic.Services
{
    public class MatchEvidenceService : AppBaseServiceGeneric<MatchEvidenceEntity, MatchEvidenceDto, MatchEvidencePost, MatchEvidenceEdit>
    {
        public MatchEvidenceService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<MatchEvidenceEntity> validator,
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

        protected override IRepository<MatchEvidenceEntity> GetRepository()
            => this.AppUnitOfWork.MatchEvidenceRepository;
    }
}