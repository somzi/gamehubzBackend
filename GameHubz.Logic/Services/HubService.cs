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

        public async Task<IEnumerable<HubDto>> GetById(Guid id)
        {
            var entities = await this.AppUnitOfWork.HubRepository.GetWithDetailsById(id);

            return this.Mapper.Map<List<HubDto>>(entities);
        }

        protected override IRepository<HubEntity> GetRepository()
        {
            return this.AppUnitOfWork.HubRepository;
        }
    }
}