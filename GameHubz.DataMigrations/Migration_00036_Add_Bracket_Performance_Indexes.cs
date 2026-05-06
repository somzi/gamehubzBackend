using FluentMigrator;

namespace GameHubz.DataMigrations
{
    [Migration(36, "Add indexes on TournamentStage.TournamentId and TournamentGroup.TournamentStageId to speed up bracket loading for large tournaments")]
    public class Migration_00036_Add_Bracket_Performance_Indexes : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Speeds up the split-query that loads all stages for a tournament (128-player brackets hit this every time)
            if (!Schema.Table("TournamentStage").Index("IX_TournamentStage_TournamentId").Exists())
                Create.Index("IX_TournamentStage_TournamentId")
                    .OnTable("TournamentStage")
                    .OnColumn("TournamentId").Ascending();

            // Speeds up group lookup when loading stage groups
            if (!Schema.Table("TournamentGroup").Index("IX_TournamentGroup_TournamentStageId").Exists())
                Create.Index("IX_TournamentGroup_TournamentStageId")
                    .OnTable("TournamentGroup")
                    .OnColumn("TournamentStageId").Ascending();

            // Speeds up per-participant group membership lookups (used in batched standings query)
            if (!Schema.Table("TournamentParticipant").Index("IX_TournamentParticipant_TournamentGroupId").Exists())
                Create.Index("IX_TournamentParticipant_TournamentGroupId")
                    .OnTable("TournamentParticipant")
                    .OnColumn("TournamentGroupId").Ascending();
        }
    }
}
