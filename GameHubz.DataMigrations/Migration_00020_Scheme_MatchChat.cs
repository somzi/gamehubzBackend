using System.Data;

namespace GameHubz.DataMigrations
{
    [Migration(20, "Create MatchChat")]
    public class Migration_00020_Scheme_MatchChat : ForwardOnlyMigration
    {
        public override void Up()
        {
            this.Create.TableWithCommonColumns("MatchChat")
                .WithColumn("Content").AsString().NotNullable()
                .WithColumn("MatchId").AsGuid().Nullable()
                    .ForeignKey("Match", "Id").OnDeleteOrUpdate(Rule.None)
                .WithColumn("UserId").AsGuid().Nullable()
                    .ForeignKey("User", "Id").OnDeleteOrUpdate(Rule.None);
        }
    }
}