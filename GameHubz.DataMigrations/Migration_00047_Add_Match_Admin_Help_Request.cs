namespace GameHubz.DataMigrations
{
    [Migration(47, "Add admin-help request flag to Match")]
    public class Migration_00047_Add_Match_Admin_Help_Request : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Match: a participant can flag the match as needing admin attention.
            // True flag + requester/timestamp marks an open request; resolving clears them.
            Alter.Table("Match").AddColumn("AdminHelpRequested").AsBoolean().NotNullable().WithDefaultValue(false);
            Alter.Table("Match").AddColumn("AdminHelpRequestedByUserId").AsGuid().Nullable();
            // datetime2 matches RoundDeadline/RoundOpenAt on the same table (precision over compatibility).
            Alter.Table("Match").AddColumn("AdminHelpRequestedOn").AsDateTime2().Nullable();
        }
    }
}
