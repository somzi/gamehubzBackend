namespace GameHubz.DataMigrations
{
    [Migration(82, "Add OpponentNotifiedOn to Match so the 'your opponent is decided' push fires exactly once per fixture")]
    public class Migration_00082_Add_Match_Opponent_Notified : ForwardOnlyMigration
    {
        public override void Up()
        {
            // One-shot marker, same shape as the RoundReminder / ...ReminderSentOn markers the
            // deadline sweep already uses: stamped the moment both players have been told who they
            // are playing, so a sweep that runs every minute tells them once and not sixty times
            // an hour.
            Alter.Table("Match").AddColumn("OpponentNotifiedOn").AsDateTime2().Nullable();

            // Backfill every row that exists today. Without it the first sweep after deploy would
            // treat every live knockout fixture as freshly drawn and push to both of its players —
            // an announcement about a match they may have arranged days ago. Only pairings decided
            // from here on are news.
            Execute.Sql(@"UPDATE ""Match"" SET ""OpponentNotifiedOn"" = NOW() AT TIME ZONE 'UTC';");

            // The sweep asks for fixtures that are still unplayed, fully drawn and untold. Partial
            // on the marker: once a fixture has been announced it leaves the index for good, so this
            // stays small no matter how large Match grows.
            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_Match_OpponentNotified_Pending""
                ON ""Match"" (""TournamentId"")
                WHERE ""OpponentNotifiedOn"" IS NULL AND ""IsDeleted"" = FALSE;");
        }
    }
}
