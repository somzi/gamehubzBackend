using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    public class TournamentEntity : BaseEntity
    {
        public Guid? HubId { get; set; }

        public string Name { get; set; } = "";

        public string? Description { get; set; }

        public string? Rules { get; set; }

        public string? Status { get; set; }

        public string? MaxPlayers { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime? RegistrationDeadline { get; set; }

        public HubEntity? Hub { get; set; }

        public List<TournamentRegistrationEntity>? TournamentRegistrations { get; set; } = new();

        public List<MatchEntity>? Matches { get; set; } = new();
    }
}