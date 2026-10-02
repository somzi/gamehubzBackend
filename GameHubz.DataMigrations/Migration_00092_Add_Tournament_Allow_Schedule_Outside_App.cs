namespace GameHubz.DataMigrations
{
    [Migration(92, "Per-tournament switch for agreeing a match time outside the app")]
    public class Migration_00092_Add_Tournament_Allow_Schedule_Outside_App : ForwardOnlyMigration
    {
        public override void Up()
        {
            // On by default. True on every existing row, which is exactly how they behave today: a
            // participant can skip the availability step with "Agreed outside the app".
            Alter.Table("Tournament")
                .AddColumn("AllowScheduleOutsideApp").AsBoolean().NotNullable().WithDefaultValue(true);
        }
    }
}
