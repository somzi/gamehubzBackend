using GameHubz.Common.Enums;
using GameHubz.Logic.Utility;
using Microsoft.Extensions.Configuration;

namespace GameHubz.Logic.Mappings
{
    public class TournamentParticipantProfile : Profile
    {
        public TournamentParticipantProfile()
        {
        }

        public TournamentParticipantProfile(ILocalizationService localizationService)
        {
           this.CreateMap<TournamentParticipantEntity, TournamentParticipantDto>()
            ;
           this.CreateMap<TournamentParticipantEntity, TournamentParticipantEdit>();
           this.CreateMap<TournamentParticipantPost, TournamentParticipantEntity>();
        }
    }
}