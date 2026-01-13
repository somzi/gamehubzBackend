using Microsoft.Extensions.Configuration;
using GameHubz.Localization;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Test.Interfaces;

namespace GameHubz.Logic.Test.Factories
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
