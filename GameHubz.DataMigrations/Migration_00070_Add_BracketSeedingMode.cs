namespace GameHubz.DataMigrations
{
    [Migration(70, "Add BracketSeedingMode to Tournament — records how the bracket draw was made (random / manual / seeded / pots)")]
    public class Migration_00070_Add_BracketSeedingMode : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Nullable: existing rows stay null. Every bracket generated before the draw picker
            // shipped was a random shuffle, so the client renders null as "Random".
            // 1 = Random, 2 = Manual, 3 = Seeded, 4 = Pots
            // (mirrors GameHubz.DataModels.Enums.BracketSeedingMode).
            Alter.Table("Tournament")
                .AddColumn("BracketSeedingMode").AsInt32().Nullable();
        }
    }
}
