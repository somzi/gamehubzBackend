using FluentValidation;
using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class TournamentRegistrationService : AppBaseServiceGeneric<TournamentRegistrationEntity, TournamentRegistrationDto, TournamentRegistrationPost, TournamentRegistrationEdit>
    {
        public TournamentRegistrationService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<TournamentRegistrationEntity> validator,
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

        protected override async Task BeforeDtoMapToEntity(TournamentRegistrationPost inputDto, bool isNew)
        {
            inputDto.Status = RegistrationStatus.Pending;

            await Task.CompletedTask;
        }

        protected override IRepository<TournamentRegistrationEntity> GetRepository()
            => this.AppUnitOfWork.TournamentRegistrationRepository;
    }
}