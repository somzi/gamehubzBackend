using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// One open admin-help request, shaped for the tournament admin's "problematic matches" list.
    /// </summary>
    public class MatchAdminHelpItemDto
    {
        public Guid MatchId { get; set; }
        // Set when the requested match is a team-tournament sub-match — admins can then open
        // the parent team-match modal (which has the player list & scores) instead of the solo
        // modal, which only renders a single home-vs-away pair.
        public Guid? TeamMatchId { get; set; }
        public int? RoundNumber { get; set; }
        public MatchStatus Status { get; set; }
        public DateTime? ScheduledStartTime { get; set; }

        public Guid? RequestedByUserId { get; set; }
        public string? RequestedByUsername { get; set; }
        public DateTime? RequestedOn { get; set; }

        public Guid? HomeUserId { get; set; }
        public string? HomeUsername { get; set; }
        public string? HomeAvatarUrl { get; set; }
        public Guid? AwayUserId { get; set; }
        public string? AwayUsername { get; set; }
        public string? AwayAvatarUrl { get; set; }
    }
}
