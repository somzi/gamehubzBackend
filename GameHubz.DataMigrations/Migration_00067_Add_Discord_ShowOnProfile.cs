namespace GameHubz.DataMigrations
{
    [Migration(67, "Add Discord show-on-profile flag to User")]
    public class Migration_00067_Add_Discord_ShowOnProfile : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Lets a user expose their linked Discord as a public profile link (discord.com/users
            // deep link), decoupled from bot DM notifications (DiscordDmEnabled). Defaults TRUE so
            // already-linked users show up without any action; they can hide it from the Socials
            // screen. Only meaningful while DiscordUserId is set.
            Execute.Sql(@"
                ALTER TABLE ""User"" ADD COLUMN IF NOT EXISTS ""DiscordShowOnProfile"" BOOLEAN NOT NULL DEFAULT TRUE;");
        }
    }
}
