namespace GameHubz.DataModels.Models
{
    public class TournamentEdit
    {
        public Guid? Id { get; set; }

        public Guid? HubId { get; set; }

        public string Name { get; set; } = "";

        public string? Description { get; set; }

        public string? Rules { get; set; }

        public string? Status { get; set; }

        public int MaxPlayers { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime? RegistrationDeadline { get; set; }

        public HubEdit? Hub { get; set; }

        public List<TournamentRegistrationEdit>? TournamentRegistrations { get; set; } = new();

        public List<MatchEdit>? Matches { get; set; } = new();
        public List<TournamentStageEdit>? TournamentStages { get; set; } = new();

        public List<TournamentParticipantEdit>? TournamentParticipants { get; set; } = new();
    }
}