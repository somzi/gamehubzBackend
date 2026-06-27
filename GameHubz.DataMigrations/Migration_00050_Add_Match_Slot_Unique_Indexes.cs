namespace GameHubz.DataMigrations
{
    [Migration(50, "Unique match-slot indexes — DB backstop against concurrent duplicate round / bracket generation")]
    public class Migration_00050_Add_Match_Slot_Unique_Indexes : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Two partial indexes because Postgres unique indexes treat NULLs as distinct:
            //   • grouped matches (league / group stage / Swiss) are unique per
            //     (stage, group, round, order) — group stages legitimately repeat
            //     (round, order) across their groups;
            //   • bracket matches (single/double elimination, play-in, knockout) carry no
            //     group and are unique per (stage, round, order).
            // Soft-deleted rows are excluded so a regenerated match may reuse a freed slot.
            // If either CREATE fails on existing data, the table already contains duplicates
            // from a past concurrency incident — clean those rows up before re-running.
            Execute.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""UX_Match_Slot_Grouped""
                ON ""Match"" (""TournamentStageId"", ""TournamentGroupId"", ""RoundNumber"", ""MatchOrder"")
                WHERE ""TournamentGroupId"" IS NOT NULL AND ""IsDeleted"" = FALSE;");

            Execute.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""UX_Match_Slot_Ungrouped""
                ON ""Match"" (""TournamentStageId"", ""RoundNumber"", ""MatchOrder"")
                WHERE ""TournamentGroupId"" IS NULL AND ""IsDeleted"" = FALSE;");
        }
    }
}
