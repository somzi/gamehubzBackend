namespace GameHubz.DataModels.Models
{
    public class TournamentParticipantOverview
    {
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public Guid UserId { get; set; }
        public bool IsTeamTournament { get; set; }
        public Guid? TeamId { get; set; }
        public string? TeamName { get; set; }
        public Guid? CaptainUserId { get; set; }
        public int MemberCount { get; set; }
        public int? TeamSize { get; set; }

        /// <summary>Members in the lineup — equals <see cref="MemberCount"/> when there is no bench.</summary>
        public int StarterCount { get; set; }

        /// <summary>Members on the bench.</summary>
        public int ReserveCount { get; set; }

        public List<TournamentParticipantMemberOverview> Members { get; set; } = new();
    }

    public class TournamentParticipantMemberOverview
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }

        /// <summary>On the roster but out of the lineup. Always false without reserves.</summary>
        public bool IsReserve { get; set; }
    }
}