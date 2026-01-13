using Template.Logic.Services;
using Template.Logic.Test.Interfaces;

namespace Template.Logic.Test.Factories
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