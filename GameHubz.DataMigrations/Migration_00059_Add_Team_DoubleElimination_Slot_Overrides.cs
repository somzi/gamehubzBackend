namespace GameHubz.DataMigrations
{
    [Migration(59, "Add Team Double Elimination routing columns (upper/grand-final flags + slot overrides)")]
    public class Migration_00059_Add_Team_DoubleElimination_Slot_Overrides : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Mirrors MatchEntity's DE fields on TeamMatchEntity so team tournaments can run
            // double-elimination. IsUpperBracket defaults true (existing single-elim/league/group
            // team matches are all "upper"); IsGrandFinal defaults false. The slot overrides are
            // null for legacy rows => MatchOrder%2 pairing (single-elim / third-place) is preserved.
            Alter.Table("TeamMatch").AddColumn("IsUpperBracket").AsBoolean().NotNullable().WithDefaultValue(true);
            Alter.Table("TeamMatch").AddColumn("IsGrandFinal").AsBoolean().NotNullable().WithDefaultValue(false);
            Alter.Table("TeamMatch").AddColumn("IsGrandFinalReset").AsBoolean().NotNullable().WithDefaultValue(false);
            Alter.Table("TeamMatch").AddColumn("NextTeamMatchHomeAwaySlot").AsInt32().Nullable();
            Alter.Table("TeamMatch").AddColumn("NextTeamMatchLoserBracketHomeAwaySlot").AsInt32().Nullable();
        }
    }
}
