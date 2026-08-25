namespace GameHubz.DataMigrations
{
    [Migration(74, "Add RegistrationOpensAt to Tournament (scheduled registration opening)")]
    public class Migration_00074_Add_Registration_Opens_At : ForwardOnlyMigration
    {
        public override void Up()
        {
            // When set, the tournament is created as Draft and the background sweep flips it to
            // RegistrationOpen at this moment (UTC), announcing it exactly as a manual open would.
            // Null = the pre-existing behaviour: registration is open the second it is created.
            // datetime2 matches every other scheduling column on this table (RegistrationDeadline
            // via migration 4, RegistrationDeadlineReminderSentOn via migration 62).
            Alter.Table("Tournament")
                .AddColumn("RegistrationOpensAt").AsDateTime2().Nullable();

            // The sweep runs every tick and only ever cares about the handful of rows still waiting
            // to open, so a partial index keeps it off a full scan of the tournament table.
            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_Tournament_RegistrationOpensAt""
                ON ""Tournament"" (""RegistrationOpensAt"")
                WHERE ""RegistrationOpensAt"" IS NOT NULL;");
        }
    }
}
