using GameHubz.Logic.Services;
using GameHubz.Logic.Test.Interfaces;

namespace GameHubz.Logic.Test.Factories
{
    public class SearchServiceFactory : IServiceFactory<SearchService>
    {
        public SearchService CreateService()
        {
            var mapper = new MapperFactory().CreateService();
            var factory = UnitOfWorkFactoryService.Instance.UnitOfWorkFactory;
            var userContextReaderService = new UserContextReaderFactory().CreateService();
            var localizationService = new LocalizationServiceFactory().CreateService();

            var service = new SearchService(
                mapper);

            return service;
        }
    }
}
