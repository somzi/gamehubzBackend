namespace GameHubz.Logic.Mappings
{
    public class HubProfile : Profile
    {
        public HubProfile()
        {
            this.CreateMap<HubPost, HubEntity>();
            this.CreateMap<HubEntity, HubDto>()
                .ForMember(x => x.UserDisplayName, m => m.MapFrom(x => x.User.FirstName + " " + x.User.LastName));
            this.CreateMap<HubEntity, HubEdit>();
            this.CreateMap<HubEdit, HubEntity>();
        }
    }
}