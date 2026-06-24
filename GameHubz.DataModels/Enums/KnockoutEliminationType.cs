namespace GameHubz.DataModels.Enums
{
    /// <summary>
    /// Elimination style of the knockout phase that follows the group stage
    /// (<see cref="TournamentFormat.GroupStageWithKnockout"/>) or the Swiss rounds
    /// (<see cref="TournamentFormat.Swiss"/>). Null on existing rows / non-knockout formats is
    /// treated as <see cref="Single"/>. <see cref="Double"/> is solo-only — the engine has no
    /// team double-elimination.
    /// </summary>
    public enum KnockoutEliminationType
    {
        Single = 1,
        Double = 2
    }
}
