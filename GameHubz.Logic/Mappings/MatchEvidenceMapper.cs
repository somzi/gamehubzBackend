namespace GameHubz.Logic.Mappings
{
    public class MatchEvidenceProfile : Profile
    {
        public MatchEvidenceProfile()
        {
        }

        public MatchEvidenceProfile(ILocalizationService localizationService)
        {
            this.CreateMap<MatchEvidenceEntity, MatchEvidenceDto>();
            this.CreateMap<MatchEvidenceEntity, MatchEvidenceEdit>();
            this.CreateMap<MatchEvidencePost, MatchEvidenceEntity>();
        }
    }
}