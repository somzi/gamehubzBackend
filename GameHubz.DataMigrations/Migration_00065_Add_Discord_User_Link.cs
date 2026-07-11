namespace GameHubz.DataMigrations
{
    [Migration(65, "Add Discord account link fields to User")]
    public class Migration_00065_Add_Discord_User_Link : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Phase 2 of the Discord integration: OAuth-linked personal account (identify scope only —
            // we keep the Discord id + username, never the OAuth tokens) plus a per-user opt-out for
            // bot DM notifications. DmEnabled defaults to TRUE so linking alone turns DMs on.
            Execute.Sql(@"
                ALTER TABLE ""User"" ADD COLUMN IF NOT EXISTS ""DiscordUserId"" TEXT NULL;
                ALTER TABLE ""User"" ADD COLUMN IF NOT EXISTS ""DiscordUsername"" TEXT NULL;
                ALTER TABLE ""User"" ADD COLUMN IF NOT EXISTS ""DiscordDmEnabled"" BOOLEAN NOT NULL DEFAULT TRUE;");
        }
    }
}
