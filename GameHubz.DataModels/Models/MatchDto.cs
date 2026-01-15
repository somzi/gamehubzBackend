using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class MatchDto
    {
        public Guid? Id { get; set; }

        public Guid TournamentId { get; set; }

        public int? RoundNumber { get; set; }

        public Guid HomeParticipantId { get; set; }

        public Guid AwayParticipantId { get; set; }

        public int? HomeUserScore { get; set; }

        public int? AwayUserScore { get; set; }

        public DateTime? ScheduledStartTime { get; set; }

        public MatchStatus Status { get; set; }

        public Guid? WinnerParticipantId { get; set; }

        public TournamentDto? Tournament { get; set; }

        public UserDto? HomeUser { get; set; }

        public UserDto? AwayUser { get; set; }

        public UserDto? WinnerUser { get; set; }
        public Guid? TournamentStageId { get; set; }
    }
}