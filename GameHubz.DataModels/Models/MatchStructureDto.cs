using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class MatchStructureDto
    {
        public Guid Id { get; set; }
        public int Round { get; set; }
        public int Order { get; set; }
        public MatchStage Stage { get; set; }
        public MatchStatus Status { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? RoundDeadline { get; set; }
        public Guid? NextMatchId { get; set; }
        public Guid? TeamMatchId { get; set; }
        public Guid? NextTeamMatchId { get; set; }
        public Guid? NextTeamMatchLoserBracketId { get; set; }

        public MatchParticipantDto? Home { get; set; }
        public MatchParticipantDto? Away { get; set; }
        public List<string> Evidences { get; set; }
        public bool IsRoundLocked { get; set; }
        public DateTime? MatchOpensAt { get; set; }
        public bool CanRevert { get; set; }

        // Pending proposal info — populated when the tournament requires result approval
        // and a participant has reported a score that has not yet been confirmed.
        public int? ProposedHomeScore { get; set; }
        public int? ProposedAwayScore { get; set; }
        public Guid? ProposedByUserId { get; set; }
    }
}