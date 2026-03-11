namespace GameHubz.DataMigrations
{
    [Migration(25, "Add RoundDeadline to Match")]
    public class Migration_00025_Add_RoundDeadline_To_Match : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("Match").AddColumn("RoundDeadline").AsDateTime2().Nullable();
        }
    }
}
