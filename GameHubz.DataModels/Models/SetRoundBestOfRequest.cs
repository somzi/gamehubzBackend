namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// Per-round series format override, the format sibling of <see cref="SetRoundDeadlineRequest"/>:
    /// the organizer stamps a Best-of on a whole round the same way they stamp a deadline or an
    /// opens-at.
    /// </summary>
    public class SetRoundBestOfRequest
    {
        public int RoundNumber { get; set; }

        /// <summary>
        /// Scopes the change to one stage. In double-elimination the Winners and Losers brackets are
        /// separate stages whose rounds share a RoundNumber, so an unscoped update would hit both.
        /// </summary>
        public Guid? StageId { get; set; }

        /// <summary>Games this round is played over. Ignored when <see cref="ClearBestOf"/> is set.</summary>
        public int? BestOf { get; set; }

        /// <summary>Games the tiebreak replay is played over. Null = replay the round's own format.</summary>
        public int? TiebreakBestOf { get; set; }

        /// <summary>Drops the round override so the round falls back to the tournament default.</summary>
        public bool ClearBestOf { get; set; }
    }

    /// <summary>
    /// What a round-format change actually did. Matches that already have a recorded game keep
    /// their format — the count of those is reported back so the UI can say so plainly instead of
    /// pretending the whole round changed.
    /// </summary>
    public class SetRoundBestOfResult
    {
        public int UpdatedMatches { get; set; }

        public int SkippedLockedMatches { get; set; }
    }
}
