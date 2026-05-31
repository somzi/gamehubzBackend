namespace GameHubz.Logic.Mappings
{
    public class UserHubRequestProfile : Profile
    {
        public UserHubRequestProfile()
        {
        }

        public UserHubRequestProfile(ILocalizationService localizationService)
        {
            this.CreateMap<UserHubRequestEntity, UserHubRequestDto>()
                .ForMember(x => x.RequestId, m => m.MapFrom(x => x.Id!.Value))
                .ForMember(x => x.UserId, m => m.MapFrom(x => x.UserId!.Value))
                .ForMember(x => x.HubId, m => m.MapFrom(x => x.HubId!.Value))
                .ForMember(x => x.Username, m => m.MapFrom(x => x.User != null ? x.User.Username : ""))
                .ForMember(x => x.AvatarUrl, m => m.MapFrom(x => x.User != null ? x.User.AvatarUrl : null))
                .ForMember(x => x.RequestedAt, m => m.MapFrom(x => x.CreatedOn ?? DateTime.UtcNow));
            this.CreateMap<UserHubRequestEntity, UserHubRequestEdit>();
            this.CreateMap<UserHubRequestPost, UserHubRequestEntity>();
            this.CreateMap<UserHubRequestEdit, UserHubRequestEntity>();
        }
    }
}
