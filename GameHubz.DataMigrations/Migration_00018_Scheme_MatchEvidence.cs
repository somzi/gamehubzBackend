using System.Data;

namespace GameHubz.DataMigrations
{
    [Migration(18, "Create MatchEvidence")]
    public class Migration_00018_Scheme_MatchEvidence : ForwardOnlyMigration
    {
        public override void Up()
        {
            this.Create.TableWithCommonColumns("MatchEvidence")
                .WithColumn("Url").AsString().Nullable()
                .WithColumn("MatchId").AsGuid().Nullable()
                    .ForeignKey("Match", "Id").OnDeleteOrUpdate(Rule.None);
        }
    }
}