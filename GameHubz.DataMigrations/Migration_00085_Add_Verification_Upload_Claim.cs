namespace GameHubz.DataMigrations
{
    [Migration(85, "Result verification: upload claim, so a retried recording upload is stored once")]
    public class Migration_00085_Add_Verification_Upload_Claim : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Its own migration rather than an edit of 84: anyone who already ran 84 against a local
            // database would otherwise never get the column. IF NOT EXISTS keeps it harmless either way.
            Execute.Sql(@"
                ALTER TABLE ""MatchResultVerification""
                ADD COLUMN IF NOT EXISTS ""EvidenceUploadClaimedOn"" TIMESTAMP NULL;");
        }
    }
}
