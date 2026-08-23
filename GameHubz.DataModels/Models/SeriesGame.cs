namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// One game inside a best-of series, as stored in <c>Match.GamesJson</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="SeriesNumber"/> groups the games into consecutive series: 1 is the main series,
    /// 2 is the first tiebreak, 3 the second, and so on. A tiebreak series only exists when the
    /// series before it finished level, so the UI can render "Main series / Tiebreak 1 / Tiebreak 2"
    /// blocks without a second grouping field. Drawn games are allowed, which is why a series can
    /// finish level at any Best-of — even an odd one.
    /// </remarks>
    public class SeriesGame
    {
        public int HomeScore { get; set; }

        public int AwayScore { get; set; }

        /// <summary>1 = main series, 2 = first tiebreak, 3 = second tiebreak, …</summary>
        public int SeriesNumber { get; set; } = 1;
    }
}
