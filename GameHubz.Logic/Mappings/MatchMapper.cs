namespace GameHubz.Logic.Mappings
{
    public class MatchProfile : Profile
    {
        public MatchProfile()
        {
        }

        public MatchProfile(ILocalizationService localizationService)
        {
            this.CreateMap<MatchEntity, MatchDto>();
            this.CreateMap<MatchEntity, MatchEdit>();
            this.CreateMap<MatchPost, MatchEntity>();
            this.CreateMap<MatchEntity, MatchListItemDto>()
              .ForMember(dest => dest.IsWin, opt => opt.Ignore());
        }
    }
}