namespace GameHubz.DataMigrations
{
    [Migration(95, "Separate verification records for each game in a series")]
    public class Migration_00095_Add_Per_Game_Verification : ForwardOnlyMigration
    {
        public override void Up()
        {
            // 0 = a proof of the whole match (MatchResultVerificationEntity.WholeMatch). Every existing
            // record was made under the old rule — one verification per player per match — so it keeps
            // covering the whole match instead of being narrowed down to game 1.
            Execute.Sql(@"
                ALTER TABLE ""MatchResultVerification""
                ADD COLUMN IF NOT EXISTS ""SeriesNumber"" INTEGER NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS ""GameNumber"" INTEGER NOT NULL DEFAULT 0;
                CREATE INDEX IF NOT EXISTS ""IX_Verification_Game""
                ON ""MatchResultVerification"" (""MatchId"", ""UserId"", ""SeriesNumber"", ""GameNumber"", ""Status"");");
        }
    }
}
