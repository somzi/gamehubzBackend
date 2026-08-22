namespace GameHubz.DataModels.Enums
{
    public enum MatchStatus
    {
        Pending = 1,
        Scheduled = 2,
        Live = 3,
        Completed = 4,
        NoShow = 5,

        // Solo knockout only: the series finished level, so the match is reported but undecided
        // and awaiting a tiebreak series. League / group / Swiss keep a level series as a plain
        // draw (Completed, no winner); team ties resolve through TeamMatchStatus.TieBreakRequired.
        TieBreakRequired = 6
    }
}