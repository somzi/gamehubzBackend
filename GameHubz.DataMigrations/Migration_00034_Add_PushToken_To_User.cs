using FluentMigrator;

namespace GameHubz.DataMigrations
{
    [Migration(34, "Add PushToken to User")]
    public class Migration_00034_Add_PushToken_To_User : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("User").AddColumn("PushToken").AsString(255).Nullable();
        }
    }
}
