namespace GameHubz.DataMigrations
{
    [Migration(64, "Add Discord webhook integration fields to Hub")]
    public class Migration_00064_Add_Discord_Webhook_To_Hub : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Phase 1 of the Discord integration: one webhook URL per hub (deliberately a single
            // column, not a table of webhooks) plus a JSON blob of per-event on/off switches, e.g.
            // { "registrationOpened": true, "matchApproved": false, ... }. Both nullable — hubs
            // without a webhook keep NULL and the notifiers skip the Discord branch entirely.
            Execute.Sql(@"
                ALTER TABLE ""Hub"" ADD COLUMN IF NOT EXISTS ""DiscordWebhookUrl"" TEXT NULL;
                ALTER TABLE ""Hub"" ADD COLUMN IF NOT EXISTS ""DiscordNotificationSettings"" TEXT NULL;");
        }
    }
}
