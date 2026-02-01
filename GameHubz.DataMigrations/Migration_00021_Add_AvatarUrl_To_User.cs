namespace GameHubz.DataMigrations
{
    [Migration(21, "Add AvatarUrl to User")]
    public class Migration_00021_Add_AvatarUrl_To_User : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("User").AddColumn("AvatarUrl").AsString(500).Nullable();
        }
    }
}