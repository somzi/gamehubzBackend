namespace GameHubz.DataMigrations
{
    [Migration(58, "Add KnockoutEliminationType to Tournament (single/double knockout for Groups/Swiss)")]
    public class Migration_00058_Add_KnockoutEliminationType : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Nullable: existing rows stay null => treated as single elimination (current behavior).
            // 1 = Single, 2 = Double (mirrors GameHubz.DataModels.Enums.KnockoutEliminationType).
            Alter.Table("Tournament")
                .AddColumn("KnockoutEliminationType").AsInt32().Nullable();
        }
    }
}
