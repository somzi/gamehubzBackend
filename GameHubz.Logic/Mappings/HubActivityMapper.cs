namespace GameHubz.Logic.Mappings
{
    public class HubActivityProfile : Profile
    {
        public HubActivityProfile()
        {
        }

        public HubActivityProfile(ILocalizationService localizationService)
        {
            this.CreateMap<HubActivityEntity, HubActivityDto>();
            this.CreateMap<HubActivityEntity, HubActivityEdit>();
            this.CreateMap<HubActivityPost, HubActivityEntity>();
        }
    }
}