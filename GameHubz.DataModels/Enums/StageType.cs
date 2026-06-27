namespace GameHubz.DataModels.Enums
{
    public enum StageType
    {
        GroupStage = 1,
        League = 2,
        SingleEliminationBracket = 3,
        DoubleEliminationWinnersBracket = 4,
        DoubleEliminationLosersBracket = 5,
        Swiss = 6,

        // Single round of cross-seeded matches between two Swiss qualification zones;
        // its winners join the direct qualifiers in the knockout bracket.
        PlayIn = 7
    }
}