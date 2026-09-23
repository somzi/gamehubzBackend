using System.Data;

namespace GameHubz.DataMigrations
{
    [Migration(86,"Match chat messages and read cursors cascade with their match")]
    public class Migration_00086_Cascade_Match_Chat_On_Match_Delete : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Hard-deleting a match (resetting a knockout, rebuilding a tie's games, dropping a grand-final
            // reset) failed with 23503 the moment anyone had opened that match's chat: opening the chat tab
            // upserts a MatchChatRead cursor, and neither foreign key below had a delete rule. The EF model
            // already declared Cascade for the cursor; the database never got it.
            //
            // A thread and its cursors belong to one pairing, so they go when the match goes. A cascade is a
            // real DELETE, so soft-deleted rows — which still hold the key — go too.
            //
            // MatchEvidence keeps NO ACTION on purpose: its row is the only handle on a stored file, and a
            // cascade would strand that file in storage. Callers refuse to delete a match that has evidence.
            if (Schema.Table("MatchChatRead").Constraint("FK_MatchChatRead_Match").Exists())
                Delete.ForeignKey("FK_MatchChatRead_Match").OnTable("MatchChatRead");

            Create.ForeignKey("FK_MatchChatRead_Match")
                .FromTable("MatchChatRead").ForeignColumn("MatchId")
                .ToTable("Match").PrimaryColumn("Id")
                .OnDelete(Rule.Cascade);

            // Migration 20 left this name to FluentMigrator's convention, FK_{table}_{column}_{target}_{column}
            // — the same convention migration 8 relied on to drop FK_Match_HomeUserId_User_Id.
            if (Schema.Table("MatchChat").Constraint("FK_MatchChat_MatchId_Match_Id").Exists())
                Delete.ForeignKey("FK_MatchChat_MatchId_Match_Id").OnTable("MatchChat");

            Create.ForeignKey("FK_MatchChat_MatchId_Match_Id")
                .FromTable("MatchChat").ForeignColumn("MatchId")
                .ToTable("Match").PrimaryColumn("Id")
                .OnDelete(Rule.Cascade);
        }
    }
}
