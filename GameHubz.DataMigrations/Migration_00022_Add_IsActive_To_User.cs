namespace GameHubz.DataMigrations
{
    [Migration(22, "Add IsActive to User")]
    public class Migration_00022_Add_IsActive_To_User : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("User").AddColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true);
        }
    }
}