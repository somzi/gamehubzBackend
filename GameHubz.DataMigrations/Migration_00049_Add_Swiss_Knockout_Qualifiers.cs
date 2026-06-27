namespace GameHubz.DataMigrations
{
    [Migration(49, "Add Swiss knockout / play-in qualifier columns to Tournament")]
    public class Migration_00049_Add_Swiss_Knockout_Qualifiers : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Knockout bracket size after the Swiss rounds (power of 2). Null = pure Swiss.
            Alter.Table("Tournament").AddColumn("SwissKnockoutQualifiers").AsInt32().Nullable();

            // Direct knockout berths from the standings; remaining slots are decided by a
            // play-in round between standings D+1 .. D+2(N-D). Null / == N = no play-in.
            Alter.Table("Tournament").AddColumn("SwissDirectQualifiers").AsInt32().Nullable();
        }
    }
}
