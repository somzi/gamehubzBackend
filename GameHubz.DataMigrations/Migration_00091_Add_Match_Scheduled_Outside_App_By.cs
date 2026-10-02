namespace GameHubz.DataMigrations
{
    [Migration(91, "Record who marked a match as agreed outside the app")]
    public class Migration_00091_Add_Match_Scheduled_Outside_App_By : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("Match").AddColumn("ScheduledOutsideAppByUserId").AsGuid().Nullable();

            // Rows already marked before this column existed. SetScheduled stamps the kick-off, the
            // ready-check marker and ModifiedOn with the same instant and ModifiedBy with the presser,
            // so a scheduled row that still carries all four untouched names who pressed it. A row
            // written since (a result proposal, an edit) no longer does and stays unmarked.
            Execute.Sql(@"
                UPDATE ""Match""
                SET ""ScheduledOutsideAppByUserId"" = ""ModifiedBy""
                WHERE ""Status"" = 2 -- MatchStatus.Scheduled
                  AND ""ScheduledStartTime"" IS NOT NULL
                  AND ""CheckInResolvedOn"" = ""ScheduledStartTime""
                  AND ""ModifiedOn"" = ""ScheduledStartTime""
                  AND ""ModifiedBy"" IS NOT NULL;");
        }
    }
}
