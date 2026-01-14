using FluentMigrator;

namespace GameHubz.DataMigrations
{
    [Migration(7, "Add Username column to User table")]
    public class Migration_00007_Add_Username_To_User : Migration
    {
        public override void Up()
        {
            Alter.Table("User")
                .AddColumn("Username").AsString(100).NotNullable().WithDefaultValue("");
        }

        public override void Down()
        {
            Delete.Column("Username").FromTable("User");
        }
    }
}
