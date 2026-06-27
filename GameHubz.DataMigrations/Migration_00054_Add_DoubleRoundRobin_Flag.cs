namespace GameHubz.DataMigrations
{
    [Migration(54, "Add DoubleRoundRobin flag to Tournament — each pair plays twice (home + away) in League and Group-Stage formats")]
    public class Migration_00054_Add_DoubleRoundRobin_Flag : ForwardOnlyMigration
    {
        public override void Up()
        {
            // When true, league and group-stage formats generate reverse-fixture rounds:
            // every pair plays twice instead of once. Existing tournaments default to false.
            Alter.Table("Tournament").AddColumn("DoubleRoundRobin").AsBoolean().NotNullable().WithDefaultValue(false);
        }
    }
}
