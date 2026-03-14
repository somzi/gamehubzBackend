namespace GameHubz.DataMigrations
{
    [Migration(27, "Add RoundOpenAt to Match")]
    public class Migration_00027_Add_RoundOpenAt_To_Match : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("Match").AddColumn("RoundOpenAt").AsDateTime2().Nullable();
        }
    }
}