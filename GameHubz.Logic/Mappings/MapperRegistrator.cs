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
            mapperConfiguration.AddProfile(new HubProfile());

            //***********************************************
            //********** GENERATED **************************
            //***********************************************

            mapperConfiguration.AddProfile(new UserHubProfile(localizationService));
            mapperConfiguration.AddProfile(new TournamentProfile(localizationService));
            mapperConfiguration.AddProfile(new TournamentRegistrationProfile(localizationService));
            mapperConfiguration.AddProfile(new MatchProfile(localizationService));

            // DO NOT DELETE - Generated Mappers Tag
        }
    }
}