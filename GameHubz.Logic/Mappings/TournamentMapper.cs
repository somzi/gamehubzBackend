namespace GameHubz.Logic.Mappings
{
    public class TournamentProfile : Profile
    {
        public TournamentProfile()
        {
        }

        public TournamentProfile(ILocalizationService localizationService)
        {
            this.CreateMap<TournamentEntity, TournamentDto>();
            this.CreateMap<TournamentEntity, TournamentEdit>();
            this.CreateMap<TournamentPost, TournamentEntity>();
        }
    }
}