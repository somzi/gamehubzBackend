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

        public DateTime? RegistrationOpensAt { get; set; }

        public bool HasThirdPlaceMatch { get; set; }

        public bool RequireResultApproval { get; set; }

        /// <summary>Ready check on scheduled matches — see TournamentEntity.RequireMatchCheckIn.</summary>
        public bool RequireMatchCheckIn { get; set; }

        /// <summary>Grace minutes for the ready check. Null = the system default.</summary>
        public int? CheckInGraceMinutes { get; set; }

        /// <summary>Result verification before a report — see TournamentEntity.RequireResultVerification.</summary>
        public bool RequireResultVerification { get; set; }

        public HubEdit? Hub { get; set; }

        public List<TournamentRegistrationEdit>? TournamentRegistrations { get; set; } = new();

        public List<MatchEdit>? Matches { get; set; } = new();
        public List<TournamentStageEdit>? TournamentStages { get; set; } = new();

        public List<TournamentParticipantEdit>? TournamentParticipants { get; set; } = new();
    }
}