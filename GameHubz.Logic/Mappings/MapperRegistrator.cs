using Microsoft.Extensions.Configuration;

namespace GameHubz.Logic.Mappings
{
    public class MapperRegistrator
    {
        public static void Register(
            IMapperConfigurationExpression mapperConfiguration,
            ILocalizationService localizationService,
            IConfiguration configuration)
        {
            mapperConfiguration.AddProfile(new AssetProfile(configuration));
            mapperConfiguration.AddProfile(new UserProfile());
            mapperConfiguration.AddProfile(new EmailQueueProfile());

            //***********************************************
            //********** GENERATED **************************
            //***********************************************

            // DO NOT DELETE - Generated Mappers Tag
        }
    }
}
