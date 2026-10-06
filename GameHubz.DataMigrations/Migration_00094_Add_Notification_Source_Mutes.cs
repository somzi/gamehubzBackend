namespace GameHubz.DataMigrations
{
    [Migration(94, "Personal hub and tournament notification exclusions")]
    public class Migration_00094_Add_Notification_Source_Mutes : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("User")
                .AddColumn("MutedHubIdsJson").AsCustom("text").Nullable()
                .AddColumn("MutedTournamentIdsJson").AsCustom("text").Nullable();
        }
    }
}
