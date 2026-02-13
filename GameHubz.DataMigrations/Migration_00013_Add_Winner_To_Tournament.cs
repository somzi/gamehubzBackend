namespace GameHubz.DataMigrations
{
    [Migration(13, "Add Winner to Tournament")]
    public class Migration_00013_Add_Winner_To_Tournament : ForwardOnlyMigration
    {
        public override void Up()
        {
            this.Alter.Table("Tournament")
             .AddColumn("WinnerUserId").AsGuid().Nullable();
        }
    }
}
