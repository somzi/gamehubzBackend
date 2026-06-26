namespace GameHubz.DataMigrations
{
    [Migration(62, "Add deadline-reminder tracking columns to Tournament and Match (background push reminders)")]
    public class Migration_00062_Add_Deadline_Reminder_Tracking : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Set once the single "registration closing soon" push has been sent for the
            // tournament, so the background sweep never reminds the same hub twice. Null = unsent.
            Alter.Table("Tournament")
                .AddColumn("RegistrationDeadlineReminderSentOn").AsDateTime2().Nullable();

            // How far the round-deadline reminders have progressed for this match:
            // 0 = none, 1 = early (24h) sent, 2 = last-call sent (done). Two waves, so a single
            // timestamp can't represent it. Existing matches backfill to 0.
            Alter.Table("Match")
                .AddColumn("RoundReminderStage").AsInt32().NotNullable().WithDefaultValue(0);
        }
    }
}
