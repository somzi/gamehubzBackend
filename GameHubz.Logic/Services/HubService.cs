using FluentValidation;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;
using GameHubz.Common.Interfaces;
using GameHubz.Common.Models;

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

        protected override IRepository<HubEntity> GetRepository()
        {
            return this.AppUnitOfWork.HubRepository;
        }
    }
}
