using AutoMapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Template.Logic.Interfaces;
using Template.Logic.Mappings;
using Template.Logic.Test.Interfaces;

namespace Template.Logic.Test.Factories
{
    internal class MapperFactory
        : IServiceFactory<IMapper>
    {
        public IMapper CreateService()
        {
            IConfiguration configuration = new ConfigurationFactory()
                .CreateConfigurationFromLocalJson();

            ILocalizationService localizationService = new LocalizationServiceFactory().CreateService();

            var mapperConfigExpression = new MapperConfigurationExpression();

            MapperRegistrator.Register(mapperConfigExpression, localizationService, configuration);

            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
            });

            var mapperConfig = new MapperConfiguration(mapperConfigExpression, loggerFactory);

            return mapperConfig.CreateMapper();
        }
    }
}