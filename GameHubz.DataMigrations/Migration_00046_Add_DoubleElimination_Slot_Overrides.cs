namespace GameHubz.DataMigrations
{
    [Migration(46, "Add slot-override columns for Double Elimination loser-bracket routing")]
    public class Migration_00046_Add_DoubleElimination_Slot_Overrides : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Match: explicit destination-slot overrides used by double-elimination.
            // Null preserves the legacy MatchOrder%2 pairing (single-elimination, third-place).
            // 0 = home slot, 1 = away slot.
            Alter.Table("Match").AddColumn("NextMatchHomeAwaySlot").AsInt32().Nullable();
            Alter.Table("Match").AddColumn("NextMatchLoserBracketHomeAwaySlot").AsInt32().Nullable();
        }
    }
}
