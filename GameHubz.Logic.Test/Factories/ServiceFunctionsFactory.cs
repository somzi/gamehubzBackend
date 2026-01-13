using GameHubz.Logic.Services;
using GameHubz.Logic.Test.Interfaces;

namespace GameHubz.Logic.Test.Factories
{
    public class ServiceFunctionsFactory : IServiceFactory<ServiceFunctions>
    {
        public ServiceFunctions CreateService()
        {
            var localizationSerivce = new LocalizationServiceFactory().CreateService();
            var mapper = new MapperFactory().CreateService();
            var userContextReaderService = new UserContextReaderFactory().CreateService();
            var searchService = new SearchServiceFactory().CreateService();

            var service = new ServiceFunctions(userContextReaderService, localizationSerivce, searchService, mapper);

            return service;
        }
    }
}
