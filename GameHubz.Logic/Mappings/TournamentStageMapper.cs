using GameHubz.Common.Enums;
using GameHubz.Logic.Utility;
using Microsoft.Extensions.Configuration;

namespace GameHubz.Logic.Mappings
{
    public class TournamentStageProfile : Profile
    {
        public TournamentStageProfile()
        {
        }

        public TournamentStageProfile(ILocalizationService localizationService)
        {
           this.CreateMap<TournamentStageEntity, TournamentStageDto>()
            ;
           this.CreateMap<TournamentStageEntity, TournamentStageEdit>();
           this.CreateMap<TournamentStagePost, TournamentStageEntity>();
        }
    }
}