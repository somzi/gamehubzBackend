using System.Data;

namespace GameHubz.DataMigrations
{
    [Migration(3, "Add Hub table")]
    public class Migration_00003_Scheme_Hub : ForwardOnlyMigration
    {
        public override void Up()
        {
            this.Create.TableWithCommonColumns("Hub")
                .WithColumn("Name").AsString(256).NotNullable()
                .WithColumn("Description").AsString(1000).Nullable()
                .WithColumn("UserId").AsGuid().NotNullable()
                    .ForeignKey("User", "Id").OnDeleteOrUpdate(Rule.None);
        }
    }
}