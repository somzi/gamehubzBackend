namespace GameHubz.DataMigrations
{
    [Migration(11111, "Added name to tournament stage")]
    public class Migration_00011_Added_Name_TournamentStage : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("TournamentStage").AddColumn("Name").AsString().Nullable();
        }
    }
}