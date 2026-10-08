namespace GameHubz.DataMigrations
{
    [Migration(99, "Per-tournament switch keeping the match chat shut until availability is set")]
    public class Migration_00099_Add_Tournament_Require_Availability_For_Chat : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Off by default, false on every existing row: the match chat stays open from the start,
            // exactly as it does today.
            Alter.Table("Tournament")
                .AddColumn("RequireAvailabilityForChat").AsBoolean().NotNullable().WithDefaultValue(false);
        }
    }
}
