using Microsoft.Extensions.Configuration;
using Template.Localization;
using Template.Logic.Interfaces;
using Template.Logic.Test.Interfaces;

namespace Template.Logic.Test.Factories
{
    internal class LocalizationServiceFactory
        : IServiceFactory<ILocalizationService>
    {
        public ILocalizationService CreateService()
        {
            IConfiguration config = new ConfigurationFactory().CreateConfigurationFromLocalJson();
            var localizationSerivce = new LocalizationService(config);

            return localizationSerivce;
        }
    }
}