using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class TournamentOverview
    {
        public string Name { get; set; } = string.Empty;
        public Guid Id { get; set; }
        public Guid HubId { get; set; }
        public RegionType Region { get; set; }
        public DateTime StartDate { get; set; }
        public int NumberOfParticipants { get; set; }
        public int MaxPlayers { get; set; }
        public int Prize { get; set; }
        public PrizeCurrency PrizeCurrency { get; set; }
        public TournamentStatus Status { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Rules { get; set; } = string.Empty;
        public Guid CreatedBy { get; set; }
        public DateTime? RegistrationDeadLine { get; set; }
        public string HubName { get; set; } = string.Empty;
        public string? HubAvatarUrl { get; set; }
        public TournamentFormat? Format { get; set; }
        public int? RoundDurationMinutes { get; set; }
        public bool? IsTeamTournament { get; set; }
        public int? TeamSize { get; set; }
        public TeamWinCondition? TeamWinCondition { get; set; }
    }
}