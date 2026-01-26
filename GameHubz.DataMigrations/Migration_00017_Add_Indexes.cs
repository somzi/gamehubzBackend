namespace GameHubz.DataMigrations
{
    [Migration(17, "Add indexes to tournament-related tables")]
    public class Migration_00017_Add_Indexes : ForwardOnlyMigration
    {
        public override void Up()
        {
            this.Create.Index("IX_HubActivity_HubId_CreatedOn").OnTable("HubActivity").OnColumn("HubId").Ascending().OnColumn("CreatedOn").Ascending();
            this.Create.Index("IX_Tournament_HubId_Status").OnTable("Tournament").OnColumn("HubId").Ascending().OnColumn("Status").Ascending();
            this.Create.Index("IX_Tournament_StartDate").OnTable("Tournament").OnColumn("StartDate").Ascending();
            this.Create.Index("IX_Match_TournamentStageId").OnTable("Match").OnColumn("TournamentStageId").Ascending();
            this.Create.Index("IX_Match_TournamentGroupId").OnTable("Match").OnColumn("TournamentGroupId").Ascending();
            this.Create.Index("IX_TournamentParticipant_UserId").OnTable("TournamentParticipant").OnColumn("UserId").Ascending();
            this.Create.Index("IX_TournamentParticipant_TournamentGroupId").OnTable("TournamentParticipant").OnColumn("TournamentGroupId").Ascending();
        }
    }
}