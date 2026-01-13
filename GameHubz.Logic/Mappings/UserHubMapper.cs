namespace GameHubz.Logic.Mappings
{
    public class UserHubProfile : Profile
    {
        public UserHubProfile()
        {
        }

        public UserHubProfile(ILocalizationService localizationService)
        {
            this.CreateMap<UserHubEntity, UserHubDto>();
            this.CreateMap<UserHubEntity, UserHubEdit>();
            this.CreateMap<UserHubPost, UserHubEntity>();
        }
    }
}