namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// v2 result payload: the whole series, game by game, instead of a single score line.
    /// </summary>
    /// <remarks>
    /// The server re-derives the headline score, the goal totals and the winner from
    /// <see cref="Games"/> — the client never sends a precomputed result. A Bo1 match is simply a
    /// one-game series, which keeps a single code path for every format.
    /// <para/>
    /// Tiebreaks are appended to the same list with an incremented <see cref="SeriesGame.SeriesNumber"/>,
    /// so re-reporting a match that went to a tiebreak means sending the main series plus every
    /// tiebreak series together.
    /// </remarks>
    public class MatchSeriesResultDto
    {
        public Guid MatchId { get; set; }

        public Guid TournamentId { get; set; }

        public List<SeriesGame> Games { get; set; } = new();

        /// <summary>
        /// Opt-in cascade when the edit changes the winner and the bracket has already progressed —
        /// same semantics as <see cref="MatchResultDto.Cascade"/>.
        /// </summary>
        public bool Cascade { get; set; }
    }
}
