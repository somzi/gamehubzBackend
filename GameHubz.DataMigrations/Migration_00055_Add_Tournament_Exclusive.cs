namespace GameHubz.DataMigrations
{
    // 53 is reserved by the match-streaming feature (Migration_00053_Scheme_MatchStream on
    // branch version/1.5.1, not yet merged here); using 54 avoids a version/class collision.
    [Migration(55, "Add IsExclusive to Tournament (exclusive-members-only tournaments)")]
    public class Migration_00055_Add_Tournament_Exclusive : ForwardOnlyMigration
    {
        public override void Up()
        {
            // When true, the tournament is visible/joinable only to hub members whose role is
            // Exclusive or higher (Exclusive/Admin/Owner). Default false => open to all members,
            // which backfills every existing tournament as a regular (members) tournament.
            Alter.Table("Tournament")
                .AddColumn("IsExclusive").AsBoolean().NotNullable().WithDefaultValue(false);
        }
    }
}
