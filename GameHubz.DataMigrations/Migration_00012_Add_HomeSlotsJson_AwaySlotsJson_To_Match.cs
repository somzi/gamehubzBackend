using System.Data;

namespace GameHubz.DataMigrations
{
    [Migration(12, "Add HomeSlotsJson and AwaySlotsJson to Match")]
    public class Migration_00012_Add_HomeSlotsJson_AwaySlotsJson_To_Match : ForwardOnlyMigration
    {
        public override void Up()
        {
            this.Alter.Table("Match")
                .AddColumn("HomeSlotsJson").AsMaxString().Nullable()
                .AddColumn("AwaySlotsJson").AsMaxString().Nullable();
        }
    }
}