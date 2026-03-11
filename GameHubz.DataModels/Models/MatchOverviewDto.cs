using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class MatchOverviewDto
    {
        public Guid TournamentId { get; set; }

        public string TournamentName { get; set; } = string.Empty;

        public string HubName { get; set; } = string.Empty;

        public DateTime? ScheduledTime { get; set; }

        public string OpponentName { get; set; } = string.Empty;

        public MatchStatus Status { get; set; }

        public Guid Id { get; set; }

        public Guid? HomeParticipantId { get; set; }
        public Guid? AwayParticipantId { get; set; }
        public string? OpponentAvatarUrl { get; set; }
    }
}