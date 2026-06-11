namespace GameHubz.DataMigrations
{
    [Migration(51, "Backfill ScheduledStartTime on completed matches from ModifiedOn")]
    public class Migration_00051_Backfill_ScheduledStartTime_On_Completed_Matches : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Results entered straight through the bracket never got a ScheduledStartTime,
            // so the app shows them without a date. ModifiedOn is the closest record of when
            // the result was entered. New results are stamped at finalize time in BracketService,
            // so this is a one-off backfill.
            // Byes/walkovers (one side missing) are skipped — they were never played.
            Execute.Sql(@"
                UPDATE ""Match""
                SET ""ScheduledStartTime"" = COALESCE(""ModifiedOn"", ""CreatedOn"")
                WHERE ""Status"" = 4 -- MatchStatus.Completed
                  AND ""ScheduledStartTime"" IS NULL
                  AND ""HomeParticipantId"" IS NOT NULL
                  AND ""AwayParticipantId"" IS NOT NULL;
            ");
        }
    }
}
