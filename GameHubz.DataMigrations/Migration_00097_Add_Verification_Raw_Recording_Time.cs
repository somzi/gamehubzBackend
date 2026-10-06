namespace GameHubz.DataMigrations
{
    [Migration(97, "Keep original recording metadata separate from normalized UTC time")]
    public class Migration_00097_Add_Verification_Raw_Recording_Time : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("MatchResultVerification").AddColumn("RawRecordedOn").AsDateTime().Nullable();
        }
    }
}
