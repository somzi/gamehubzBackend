namespace GameHubz.Logic.Mappings
{
    public class TournamentRegistrationProfile : Profile
    {
        public TournamentRegistrationProfile()
        {
        }

        public TournamentRegistrationProfile(ILocalizationService localizationService)
        {
            this.CreateMap<TournamentRegistrationEntity, TournamentRegistrationDto>();
            this.CreateMap<TournamentRegistrationEntity, TournamentRegistrationEdit>();
            this.CreateMap<TournamentRegistrationPost, TournamentRegistrationEntity>();
        }
    }
}