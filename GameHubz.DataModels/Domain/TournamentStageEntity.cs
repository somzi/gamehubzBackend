using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    public class TournamentStageEntity : BaseEntity
    {
        public StageType Type { get; set; }
        public int Order { get; set; }
        public int? QualifiedPlayersCount { get; set; }
        public Guid? TournamentId { get; set; }
        public TournamentEntity? Tournament { get; set; }
        public List<MatchEntity>? Matches { get; set; } = new();
        public List<TournamentGroupEntity>? TournamentGroups { get; set; } = new();
    }
}