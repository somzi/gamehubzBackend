using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    public class TournamentGroupEntity : BaseEntity
    {
        public string Name { get; set; } = "";
        public Guid? TournamentStageId { get; set; }
        public TournamentStageEntity? TournamentStage { get; set; }
        public List<TournamentParticipantEntity>? Participants { get; set; } = new();
        public List<MatchEntity>? Matches { get; set; } = new();
    }
}