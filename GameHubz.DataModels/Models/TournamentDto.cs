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

        public string? MaxPlayers { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime? RegistrationDeadline { get; set; }

        public HubDto? Hub { get; set; }

        public List<TournamentRegistrationDto>? TournamentRegistrations { get; set; } = new();

        public List<MatchDto>? Matches { get; set; } = new();
    }
}