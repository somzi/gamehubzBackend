namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// Who is on a match as committed: the two spots, the players written on the row (a team
    /// sub-match's lineup, or the players who played a solo match whose spot changed hands since),
    /// and the users holding the two spots right now. Read under the advancement lock to refuse a
    /// result write that was loaded before a swap.
    /// </summary>
    public sealed class MatchSidesRow
    {
        public Guid? HomeParticipantId { get; set; }

        public Guid? AwayParticipantId { get; set; }

        public Guid? HomeUserId { get; set; }

        public Guid? AwayUserId { get; set; }

        public Guid? HomeParticipantUserId { get; set; }

        public Guid? AwayParticipantUserId { get; set; }

        /// <summary>The tournament's status right now; null when the tournament row is gone.</summary>
        public GameHubz.DataModels.Enums.TournamentStatus? TournamentStatus { get; set; }
    }
}
