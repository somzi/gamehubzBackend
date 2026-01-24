namespace GameHubz.DataMigrations
{
    [Migration(14, "Added nickname")]
    public class Migration_00014_Add_Nickname : ForwardOnlyMigration
    {
        public override void Up()
        {
            this.Alter.Table("User")
             .AddColumn("Nickname").AsString(100).Nullable();
        }
    }
}