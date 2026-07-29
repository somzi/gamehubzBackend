namespace GameHubz.DataMigrations
{
    [Migration(71, "Add team reserves — bench slots per roster (Tournament) and the starter/reserve flag (TournamentTeamMember)")]
    public class Migration_00071_Add_Team_Reserves : ForwardOnlyMigration
    {
        public override void Up()
        {
            // TeamSize keeps its meaning: the LINEUP size, i.e. how many sub-matches a tie has.
            // AllowReserves opens extra roster slots on top of it; MaxReserves is how many.
            // A team may carry 0..MaxReserves reserves — the bench is optional, never required.
            // NOT NULL DEFAULT FALSE on AllowReserves so every existing team tournament reads as
            // "no bench" without a backfill; MaxReserves stays NULL there and is treated as 0.
            Execute.Sql(@"
                ALTER TABLE ""Tournament"" ADD COLUMN IF NOT EXISTS ""AllowReserves"" BOOLEAN NOT NULL DEFAULT FALSE;
                ALTER TABLE ""Tournament"" ADD COLUMN IF NOT EXISTS ""MaxReserves"" INTEGER NULL;");

            // IsReserve = true means "on the roster, out of the lineup": no sub-match is generated
            // for this member. Defaulting to FALSE makes every existing member a starter, which is
            // exactly what they were before the bench existed.
            Execute.Sql(@"
                ALTER TABLE ""TournamentTeamMember"" ADD COLUMN IF NOT EXISTS ""IsReserve"" BOOLEAN NOT NULL DEFAULT FALSE;");

            // The lineup is read on every sub-match generation and on every roster render, always
            // scoped to one team, so keep the starters lookup off a full member scan.
            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_TournamentTeamMember_TeamId_IsReserve""
                ON ""TournamentTeamMember"" (""TeamId"", ""IsReserve"")
                WHERE ""IsDeleted"" = FALSE;");
        }
    }
}
