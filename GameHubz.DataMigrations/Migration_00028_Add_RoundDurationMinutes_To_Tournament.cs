namespace GameHubz.DataMigrations
{
    [Migration(28, "Add RoundDurationMinutes to Tournament")]
    public class Migration_00028_Add_RoundDurationMinutes_To_Tournament : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("Tournament").AddColumn("RoundDurationMinutes").AsInt32().Nullable();
        }
    }
}