namespace GameHubz.DataMigrations
{
    [Migration(60, "Unique participant indexes — DB backstop against concurrent duplicate registration approval")]
    public class Migration_00060_Add_TournamentParticipant_Unique_Indexes : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Two partial indexes because a participant row is either solo-bound (UserId set) or
            // team-bound (TeamId set), and Postgres unique indexes treat NULLs as distinct:
            //   • solo participants are unique per (tournament, user);
            //   • team participants are unique per (tournament, team).
            // Soft-deleted rows are excluded so a removed entrant can join again later.
            // This closes the read-then-write race where two admins approve the same / a duplicate
            // registration at once and each inserts a participant — the app-level "already a
            // participant" check can't see the other transaction's uncommitted row, but the DB can.
            // If either CREATE fails on existing data, the table already holds duplicate participants
            // from a past incident — clean those rows down to one per entrant before re-running.
            Execute.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""UX_TournamentParticipant_Tournament_User""
                ON ""TournamentParticipant"" (""TournamentId"", ""UserId"")
                WHERE ""UserId"" IS NOT NULL AND ""IsDeleted"" = FALSE;");

            Execute.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""UX_TournamentParticipant_Tournament_Team""
                ON ""TournamentParticipant"" (""TournamentId"", ""TeamId"")
                WHERE ""TeamId"" IS NOT NULL AND ""IsDeleted"" = FALSE;");
        }
    }
}
