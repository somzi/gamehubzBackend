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
            mapperConfiguration.AddProfile(new UserSocialProfile(localizationService));

mapperConfiguration.AddProfile(new TournamentStageProfile(localizationService));
mapperConfiguration.AddProfile(new TournamentGroupProfile(localizationService));
mapperConfiguration.AddProfile(new TournamentParticipantProfile(localizationService));

            // DO NOT DELETE - Generated Mappers Tag
        }
    }
}
