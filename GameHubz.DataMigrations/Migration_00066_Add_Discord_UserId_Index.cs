namespace GameHubz.DataMigrations
{
    [Migration(66, "Add index on User.DiscordUserId")]
    public class Migration_00066_Add_Discord_UserId_Index : ForwardOnlyMigration
    {
        public override void Up()
        {
            // GetByDiscordUserId runs on every Discord slash command (/nextmatch, /profile) —
            // without this it's a sequential scan over User. Partial because only linked users
            // carry a value. Deliberately NOT unique: uniqueness is enforced app-side at link
            // time (DiscordLinkService), so the migration can't fail on a pre-existing duplicate.
            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_User_DiscordUserId""
                ON ""User"" (""DiscordUserId"")
                WHERE ""DiscordUserId"" IS NOT NULL;");
        }
    }
}
