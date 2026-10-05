namespace GameHubz.DataMigrations
{
    [Migration(98, "Ensure the agreed-outside-the-app column after the migration 91 renumber")]
    public class Migration_00098_Ensure_Match_Scheduled_Outside_App_By : ForwardOnlyMigration
    {
        public override void Up()
        {
            // For a database that ran this branch before the participant index moved from 91 to 93: its
            // version 91 is that index, so the real 91 (this column) never ran there and every match
            // query would fail on the missing column. Everywhere else the column exists and this is a no-op.
            Execute.Sql(@"ALTER TABLE ""Match"" ADD COLUMN IF NOT EXISTS ""ScheduledOutsideAppByUserId"" uuid NULL;");
        }
    }
}
