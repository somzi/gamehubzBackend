namespace GameHubz.DataMigrations
{
    [Migration(73, "Add a separate best-of for the knockout phase of two-phase tournaments (groups/Swiss, then a bracket)")]
    public class Migration_00073_Add_Knockout_Best_Of : ForwardOnlyMigration
    {
        public override void Up()
        {
            // NULL means "the knockout is played under the same BestOf as the phase before it",
            // which is exactly how every existing two-phase tournament behaves — so no backfill.
            // Only read for formats that actually play a bracket after a group stage or Swiss;
            // a plain Single/Double elimination tournament is one phase and uses BestOf alone.
            Execute.Sql(@"
                ALTER TABLE ""Tournament"" ADD COLUMN IF NOT EXISTS ""KnockoutBestOf"" INTEGER NULL;");
        }
    }
}
