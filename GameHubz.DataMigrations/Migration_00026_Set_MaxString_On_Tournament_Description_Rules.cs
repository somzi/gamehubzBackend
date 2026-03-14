using FluentMigrator;

namespace GameHubz.DataMigrations
{
    [Migration(26, "Set Description and Rules columns in Tournament to MaxString")]
    public class Migration_00026_Set_MaxString_On_Tournament_Description_Rules : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Column("Description").OnTable("Tournament").AsString(int.MaxValue).Nullable();
            Alter.Column("Rules").OnTable("Tournament").AsString(int.MaxValue).Nullable();
            Alter.Column("Description").OnTable("Hub").AsString(int.MaxValue).Nullable();
        }
    }
}
