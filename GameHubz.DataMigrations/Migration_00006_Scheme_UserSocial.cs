using System.Data;

namespace GameHubz.DataMigrations
{
    [Migration(6, "Create UserSocial")]
    public class Migration_00006_Scheme_UserSocial : ForwardOnlyMigration
    {
        public override void Up()
        {
            this.Create.TableWithCommonColumns("UserSocial")
                .WithColumn("Type").AsInt32().NotNullable()
                .WithColumn("Username").AsString().NotNullable()
                .WithColumn("UserId").AsGuid().Nullable()
                    .ForeignKey("User", "Id").OnDeleteOrUpdate(Rule.None);
        }
    }
}