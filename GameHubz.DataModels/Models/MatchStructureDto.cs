using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class MatchStructureDto
    {
        public Guid Id { get; set; }
        public int Round { get; set; }
        public int Order { get; set; }
        public MatchStatus Status { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? RoundDeadline { get; set; }
        public Guid? NextMatchId { get; set; }

        public MatchParticipantDto? Home { get; set; }
        public MatchParticipantDto? Away { get; set; }
        public List<string> Evidences { get; set; }
    }
}