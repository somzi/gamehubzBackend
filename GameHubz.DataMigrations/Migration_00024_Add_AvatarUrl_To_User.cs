namespace GameHubz.DataMigrations
{
    [Migration(24, "Add AvatarUrl to Hub")]
    public class Migration_00024_Add_AvatarUrl_To_User : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("Hub").AddColumn("AvatarUrl").AsString(500).Nullable();
        }
    }
}