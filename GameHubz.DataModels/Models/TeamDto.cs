using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class TeamDto
    {
        public Guid TeamId { get; set; }
        public string TeamName { get; set; } = "";
        public Guid CaptainUserId { get; set; }
        public int MemberCount { get; set; }

        /// <summary>The lineup size — how many players the team fields. Reserves sit on top of this.</summary>
        public int? TeamSize { get; set; }

        /// <summary>Whether this tournament lets rosters carry bench players at all.</summary>
        public bool AllowReserves { get; set; }

        /// <summary>Bench slots available per team when <see cref="AllowReserves"/> is on. Null = none.</summary>
        public int? MaxReserves { get; set; }

        /// <summary>Members currently in the lineup. Equals <see cref="MemberCount"/> without reserves.</summary>
        public int StarterCount { get; set; }

        /// <summary>Members currently on the bench.</summary>
        public int ReserveCount { get; set; }

        public List<TeamMemberDto> Members { get; set; } = new();
        public bool IsAlreadyRegistred { get; set; }
        public bool IsRegistrationAccepted { get; set; }
        public bool RequiresApproval { get; set; }
        public JoinRequestStatus? UserRequestStatus { get; set; }
    }

    public class TeamMemberDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = "";
        public string? AvatarUrl { get; set; }

        /// <summary>
        /// True when this member is on the bench and plays no sub-match. Always false in
        /// tournaments without reserves, so clients that ignore the field see no change.
        /// </summary>
        public bool IsReserve { get; set; }
    }

    /// <summary>Captain's lineup change: <see cref="ReserveUserId"/> takes <see cref="StarterUserId"/>'s slot.</summary>
    public class SwapLineupMemberRequest
    {
        public Guid StarterUserId { get; set; }

        public Guid ReserveUserId { get; set; }
    }
}
