namespace GameHubz.DataModels.Enums
{
    /// <summary>
    /// Which flat table the CSV export endpoint should emit. CSV holds a single table, so the
    /// two things a consumer wants — the rankings and the fixture-by-fixture results — are
    /// served as two separate datasets rather than crammed into one file with mixed schemas.
    /// </summary>
    public enum TournamentCsvDataset
    {
        /// <summary>Group / league / Swiss standings tables, one row per participant per group.</summary>
        Standings = 1,

        /// <summary>Every match in the tournament (bracket rounds included), one row per match.</summary>
        Matches = 2
    }
}
