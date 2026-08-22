namespace GameHubz.DataMigrations
{
    [Migration(72, "Add best-of series support — series format on Tournament/Match, the played games, and denormalised goal totals")]
    public class Migration_00072_Add_Best_Of_Series : ForwardOnlyMigration
    {
        public override void Up()
        {
            // BestOf NOT NULL DEFAULT 1 makes every existing tournament read as "one game decides
            // it" — the exact behaviour before series existed — with no backfill.
            // SeriesWinCondition reuses the TeamWinCondition enum values (0 = MatchWins,
            // 1 = AggregateScore); 0 is irrelevant while BestOf = 1, since a single-game series
            // always reports the raw game score under either criterion.
            // TiebreakBestOf NULL means "replay the same format" (a drawn Bo3 is settled by a Bo3).
            Execute.Sql(@"
                ALTER TABLE ""Tournament"" ADD COLUMN IF NOT EXISTS ""BestOf"" INTEGER NOT NULL DEFAULT 1;
                ALTER TABLE ""Tournament"" ADD COLUMN IF NOT EXISTS ""SeriesWinCondition"" INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE ""Tournament"" ADD COLUMN IF NOT EXISTS ""TiebreakBestOf"" INTEGER NULL;");

            // Per-match overrides, both NULL = inherit the tournament default. Populated by the
            // round-format editor (SetRoundBestOf) and stamped at bracket generation time.
            Execute.Sql(@"
                ALTER TABLE ""Match"" ADD COLUMN IF NOT EXISTS ""BestOf"" INTEGER NULL;
                ALTER TABLE ""Match"" ADD COLUMN IF NOT EXISTS ""TiebreakBestOf"" INTEGER NULL;");

            // GamesJson is the played series: [{ ""HomeScore"", ""AwayScore"", ""SeriesNumber"" }].
            // SeriesNumber 1 is the main series, 2+ are consecutive tiebreak replays. A non-empty
            // value is also the format lock — once a game is recorded, BestOf for that match is fixed.
            // ProposedGamesJson carries the same list for a pending result proposal.
            Execute.Sql(@"
                ALTER TABLE ""Match"" ADD COLUMN IF NOT EXISTS ""GamesJson"" TEXT NULL;
                ALTER TABLE ""Match"" ADD COLUMN IF NOT EXISTS ""ProposedGamesJson"" TEXT NULL;");

            // Real goals across every game. HomeUserScore keeps holding the HEADLINE score (games
            // won under MatchWins), which must not drive goal difference — standings read these.
            // NULL on every pre-existing row, where the headline IS the score, so readers fall back
            // to HomeUserScore/AwayUserScore and no backfill is needed.
            Execute.Sql(@"
                ALTER TABLE ""Match"" ADD COLUMN IF NOT EXISTS ""HomeGoalsTotal"" INTEGER NULL;
                ALTER TABLE ""Match"" ADD COLUMN IF NOT EXISTS ""AwayGoalsTotal"" INTEGER NULL;");
        }
    }
}