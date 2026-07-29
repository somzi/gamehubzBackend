namespace GameHubz.DataModels.Models
{
    public class TeamJoinData
    {
        public Guid TeamId { get; set; }
        public Guid TournamentId { get; set; }
        public Guid CaptainUserId { get; set; }
        public string TeamName { get; set; } = "";
        public int? TeamSize { get; set; }
        public int CurrentMemberCount { get; set; }

        /// <summary>Members already in the lineup — the join path fills the lineup before the bench.</summary>
        public int CurrentStarterCount { get; set; }

        /// <summary>Whether the tournament allows bench players on top of the lineup.</summary>
        public bool AllowReserves { get; set; }

        /// <summary>Bench slots per team; null/0 with <see cref="AllowReserves"/> off means none.</summary>
        public int? MaxReserves { get; set; }

        public bool UserAlreadyInTournament { get; set; }
        public bool RequiresApproval { get; set; }
        public List<TeamMemberDto> Members { get; set; } = [];

        /// <summary>Total roster slots: the lineup plus the bench this tournament grants.</summary>
        public int RosterCapacity =>
            (TeamSize ?? 0) + (AllowReserves ? Math.Max(0, MaxReserves ?? 0) : 0);
    }
}
