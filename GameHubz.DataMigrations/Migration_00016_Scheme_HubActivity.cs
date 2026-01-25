using System.Data;

namespace GameHubz.DataMigrations
{
    [Migration(16, "Create HubActivity")]
    public class Migration_00016_Scheme_HubActivity : ForwardOnlyMigration
    {
        public override void Up()
        {
            this.Create.TableWithCommonColumns("HubActivity")
                .WithColumn("Type").AsInt32().NotNullable()
                .WithColumn("HubId").AsGuid().Nullable()
                    .ForeignKey("Hub", "Id").OnDeleteOrUpdate(Rule.None)
                .WithColumn("TournamentId").AsGuid().Nullable()
                    .ForeignKey("Tournament", "Id").OnDeleteOrUpdate(Rule.None);
        }
    }
}