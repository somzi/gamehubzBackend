namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// What a round-wide double walkover actually did. The organizer confirmed a count, so they get
    /// a count back instead of a silent refresh. Both numbers are FIXTURES — a team tie is one
    /// fixture however many games it holds.
    /// </summary>
    public class RoundWalkoverResultDto
    {
        /// <summary>Fixtures closed as a double forfeit.</summary>
        public int Closed { get; set; }

        /// <summary>
        /// Fixtures deliberately left open: a reported result waiting for approval or a tie-break
        /// (the organizer must deal with those explicitly), a bracket slot still missing a side, or
        /// an elimination fixture with nowhere to advance a survivor.
        /// </summary>
        public int Skipped { get; set; }
    }
}
