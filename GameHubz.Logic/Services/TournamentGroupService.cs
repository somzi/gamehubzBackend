using FluentValidation;

namespace GameHubz.Logic.Services
{
    public class TournamentGroupService : AppBaseServiceGeneric<TournamentGroupEntity, TournamentGroupDto, TournamentGroupPost, TournamentGroupEdit>
    {
        public TournamentGroupService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<TournamentGroupEntity> validator,
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

        protected override IRepository<TournamentGroupEntity> GetRepository()
            => this.AppUnitOfWork.TournamentGroupRepository;
    }
}