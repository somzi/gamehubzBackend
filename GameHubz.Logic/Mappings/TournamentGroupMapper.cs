using GameHubz.Common.Enums;
using GameHubz.Logic.Utility;
using Microsoft.Extensions.Configuration;

namespace GameHubz.Logic.Mappings
{
    public class TournamentGroupProfile : Profile
    {
        public TournamentGroupProfile()
        {
        }

        public TournamentGroupProfile(ILocalizationService localizationService)
        {
           this.CreateMap<TournamentGroupEntity, TournamentGroupDto>()
            ;
           this.CreateMap<TournamentGroupEntity, TournamentGroupEdit>();
           this.CreateMap<TournamentGroupPost, TournamentGroupEntity>();
        }
    }
}