namespace GameHubz.Logic.Mappings
{
    public class HubProfile : Profile
    {
        public HubProfile()
        {
            this.CreateMap<HubPost, HubEntity>();
            this.CreateMap<HubEntity, HubDto>()
                .ForMember(x => x.UserDisplayName, m => m.MapFrom(x => x.User.FirstName + " " + x.User.LastName))
                .ForMember(x => x.NumberOfUsers, m => m.MapFrom(x => x.UserHubs == null ? 0 : x.UserHubs!.Count))
                .ForMember(x => x.NumberOfTournaments, m => m.MapFrom(x => x.Tournaments == null ? 0 : x.Tournaments!.Count));
            this.CreateMap<HubEntity, HubEdit>();
            this.CreateMap<HubEdit, HubEntity>();
        }
    }
}