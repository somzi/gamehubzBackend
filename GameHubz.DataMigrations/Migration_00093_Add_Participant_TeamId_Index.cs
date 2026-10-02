namespace GameHubz.DataMigrations
{
    [Migration(93, "Index participant lookup by team for player tournament lists and counts")]
    public class Migration_00093_Add_Participant_TeamId_Index : ForwardOnlyMigration
    {
        public override void Up()
        {
            // The existing (TournamentId, TeamId) index starts with TournamentId; profile
            // lookups know the player's teams across tournaments instead.
            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_TournamentParticipant_TeamId""
                    ON ""TournamentParticipant"" (""TeamId"")
                    WHERE ""TeamId"" IS NOT NULL AND ""IsDeleted"" = FALSE;");
        }
    }
}
