namespace GameHubz.DataMigrations
{
    [Migration(68, "Add Discord guild id to Hub (auto-detected from webhook URL) so slash commands can auto-resolve which hub a Discord server represents")]
    public class Migration_00068_Add_Discord_GuildId_To_Hub : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Snowflake IDs are 64-bit integers, but Discord's API always serializes them as
            // strings (JavaScript precision) — same pattern as User.DiscordUserId. Unique partial
            // index enforces "one Discord server = one hub": if a second hub tries to link to the
            // same guild, the insert fails (surfaced as a friendly 400 in HubService).
            Execute.Sql(@"
                ALTER TABLE ""Hub"" ADD COLUMN IF NOT EXISTS ""DiscordGuildId"" TEXT NULL;

                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Hub_DiscordGuildId""
                ON ""Hub"" (""DiscordGuildId"")
                WHERE ""DiscordGuildId"" IS NOT NULL;");
        }
    }
}
