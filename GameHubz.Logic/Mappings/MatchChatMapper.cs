namespace GameHubz.Logic.Mappings
{
    public class MatchChatProfile : Profile
    {
        public MatchChatProfile()
        {
        }

        public MatchChatProfile(ILocalizationService localizationService)
        {
            this.CreateMap<MatchChatEntity, MatchChatDto>();
            this.CreateMap<MatchChatEntity, MatchChatEdit>();
            this.CreateMap<MatchChatPost, MatchChatEntity>();
        }
    }
}