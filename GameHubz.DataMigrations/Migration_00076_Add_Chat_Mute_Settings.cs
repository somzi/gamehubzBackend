namespace GameHubz.DataMigrations
{
    [Migration(76, "Add per-thread match chat mute + the moderated-chat notification switch")]
    public class Migration_00076_Add_Chat_Mute_Settings : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Per-thread mute rides on the existing per-(match, user) read cursor, so muting needs
            // no table of its own. FALSE keeps every existing thread audible.
            Execute.Sql(@"
                ALTER TABLE ""MatchChatRead"" ADD COLUMN IF NOT EXISTS ""IsMuted"" BOOLEAN NOT NULL DEFAULT FALSE;");

            // Blanket switch for chats the user only moderates. TRUE preserves the behaviour
            // everyone already has; turning it off silences organizer threads without touching
            // notifications for the user's own matches.
            Execute.Sql(@"
                ALTER TABLE ""User"" ADD COLUMN IF NOT EXISTS ""ModeratedChatNotifications"" BOOLEAN NOT NULL DEFAULT TRUE;");

            // Badge recompute reads every muted match for one user; the table's unique constraint
            // leads with MatchId, so a UserId-only filter has no index to use.
            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_MatchChatRead_UserId"" ON ""MatchChatRead"" (""UserId"");");
        }
    }
}
