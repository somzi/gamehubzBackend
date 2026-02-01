namespace GameHubz.Logic.Mappings
{
    public class HubSocialProfile : Profile
    {
        public HubSocialProfile()
        {
        }

        public HubSocialProfile(ILocalizationService localizationService)
        {
            this.CreateMap<HubSocialEntity, HubSocialDto>();
            this.CreateMap<HubSocialEntity, HubSocialEdit>();
            this.CreateMap<HubSocialPost, HubSocialEntity>();
        }
    }
}