namespace GameHubz.DataMigrations
{
    [Migration(80, "Add ready-check settings to Tournament and check-in stamps to Match")]
    public class Migration_00080_Add_Match_Check_In : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Ready check, opt-in per tournament. False on every existing row, which is exactly the
            // behaviour they have today: a scheduled match is played and reported with no check-in
            // step at all.
            Alter.Table("Tournament")
                .AddColumn("RequireMatchCheckIn").AsBoolean().NotNullable().WithDefaultValue(false);

            // Minutes the opponent has to appear once the ready check is on. Null = the system
            // default (MatchCheckInRules.DefaultGraceMinutes), so an organizer who never touches
            // the field still gets a sane window.
            Alter.Table("Tournament")
                .AddColumn("CheckInGraceMinutes").AsInt32().Nullable();

            // Per-side "I am here" stamps. datetime2 matches every other timestamp on Match
            // (RoundDeadline, AdminHelpRequestedOn, HomeSlotsSetOn via migration 78).
            Alter.Table("Match").AddColumn("HomeCheckedInOn").AsDateTime2().Nullable();
            Alter.Table("Match").AddColumn("AwayCheckedInOn").AsDateTime2().Nullable();

            // Set once the sweep has ruled on the match, so it is never ruled on twice — an
            // organizer deleting the awarded result must not hand the same win straight back.
            Alter.Table("Match").AddColumn("CheckInResolvedOn").AsDateTime2().Nullable();

            // The sweep looks for scheduled matches whose kick-off has passed and that nobody has
            // ruled on yet. Partial index: the vast majority of Match rows are either unscheduled
            // or already played, and those never interest it.
            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_Match_CheckIn_Pending""
                ON ""Match"" (""ScheduledStartTime"")
                WHERE ""ScheduledStartTime"" IS NOT NULL AND ""CheckInResolvedOn"" IS NULL;");
        }
    }
}
