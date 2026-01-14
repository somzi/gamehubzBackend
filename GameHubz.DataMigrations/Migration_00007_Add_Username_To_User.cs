namespace GameHubz.DataMigrations
{
    [Migration(7, "Add Username column to User table")]
    public class Migration_00007_Add_Username_To_User : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("User")
                .AddColumn("Username").AsString(100).NotNullable().WithDefaultValue("");

            Alter.Table("User")
                .AddColumn("Region").AsInt32().NotNullable().WithDefaultValue(0);
        }
    }
}