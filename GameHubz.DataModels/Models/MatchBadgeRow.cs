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
    }
}
