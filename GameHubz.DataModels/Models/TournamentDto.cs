using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class TournamentDto
    {
        public Guid? Id { get; set; }

        public Guid? HubId { get; set; }

        public string Name { get; set; } = "";

        public string? Description { get; set; }

        public string? Rules { get; set; }

        public TournamentStatus Status { get; set; }

        public int MaxPlayers { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime? RegistrationDeadline { get; set; }
        public RegionType Region { get; set; }

        public int Prize { get; set; }
        public int? PrizeCurrency { get; set; }

        public List<TournamentRegistrationDto>? TournamentRegistrations { get; set; } = new();

        public List<MatchDto>? Matches { get; set; } = new();
        public List<TournamentStageDto>? TournamentStages { get; set; } = new();

        public List<TournamentParticipantDto>? TournamentParticipants { get; set; } = new();
    }
}