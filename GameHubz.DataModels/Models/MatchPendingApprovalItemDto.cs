using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// One match whose reported result is awaiting admin approval, shaped for the
    /// tournament admin's "pending approvals" list. A match is awaiting approval when
    /// a result has been proposed (ProposedByUserId is set) but not yet applied.
    /// </summary>
    public class MatchPendingApprovalItemDto
    {
        public Guid MatchId { get; set; }
        public int? RoundNumber { get; set; }
        public MatchStatus Status { get; set; }
        public DateTime? ScheduledStartTime { get; set; }

        public int? ProposedHomeScore { get; set; }
        public int? ProposedAwayScore { get; set; }
        public Guid? ProposedByUserId { get; set; }
        public string? ProposedByUsername { get; set; }

        public Guid? HomeUserId { get; set; }
        public string? HomeUsername { get; set; }
        public string? HomeAvatarUrl { get; set; }
        public Guid? AwayUserId { get; set; }
        public string? AwayUsername { get; set; }
        public string? AwayAvatarUrl { get; set; }
    }
}
