using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// Lightweight projection of one of the signed-in user's active matches
    /// (no includes), used to compute the Tournaments-tab badges.
    /// </summary>
    public class MatchBadgeRow
    {
        public Guid Id { get; set; }

        public MatchStatus Status { get; set; }

        // Set when an opponent proposed a result on this match (approval mode).
        // Used to count results awaiting the signed-in user's confirmation:
        // a proposal counts when it exists and was NOT made by the user.
        public Guid? ProposedByUserId { get; set; }
    }
}
