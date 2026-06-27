namespace GameHubz.DataMigrations
{
    [Migration(43, "Add Result Approval Support")]
    public class Migration_00043_Add_Result_Approval : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Tournament: when true, both participants (or admin/owner) must approve a reported result.
            Alter.Table("Tournament").AddColumn("RequireResultApproval").AsBoolean().NotNullable().WithDefaultValue(false);

            // Match: pending proposal state. Null means no proposal is awaiting approval.
            Alter.Table("Match").AddColumn("ProposedHomeScore").AsInt32().Nullable();
            Alter.Table("Match").AddColumn("ProposedAwayScore").AsInt32().Nullable();
            Alter.Table("Match").AddColumn("ProposedByUserId").AsGuid().Nullable();
        }
    }
}
