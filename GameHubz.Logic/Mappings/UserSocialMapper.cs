namespace GameHubz.Logic.Mappings
{
    public class UserSocialProfile : Profile
    {
        public UserSocialProfile()
        {
        }

        public UserSocialProfile(ILocalizationService localizationService)
        {
            this.CreateMap<UserSocialEntity, UserSocialDto>();
            this.CreateMap<UserSocialEntity, UserSocialEdit>();
            this.CreateMap<UserSocialPost, UserSocialEntity>();
        }
    }
}