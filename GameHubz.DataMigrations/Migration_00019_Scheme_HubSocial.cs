using System.Data;

namespace GameHubz.DataMigrations
{
    [Migration(19, "Create HubSocial")]
    public class Migration_00019_Scheme_HubSocial : ForwardOnlyMigration
    {
        public override void Up()
        {
            this.Create.TableWithCommonColumns("HubSocial")
                .WithColumn("Username").AsString().NotNullable()
                .WithColumn("Type").AsInt32().NotNullable()
                .WithColumn("HubId").AsGuid().Nullable()
                    .ForeignKey("Hub", "Id").OnDeleteOrUpdate(Rule.None);
        }
    }
}