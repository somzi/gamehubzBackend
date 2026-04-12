using FluentMigrator;

namespace GameHubz.DataMigrations
{
    [Migration(35, "Add index on MatchChat.MatchId for chat history queries")]
    public class Migration_00035_Add_MatchChat_MatchId_Index : ForwardOnlyMigration
    {
        public override void Up()
        {
            // GetByMatchId filters and sorts entirely on MatchId + CreatedOn
            Create.Index("IX_MatchChat_MatchId_CreatedOn")
                .OnTable("MatchChat")
                .OnColumn("MatchId").Ascending()
                .OnColumn("CreatedOn").Ascending();
        }
    }
}
