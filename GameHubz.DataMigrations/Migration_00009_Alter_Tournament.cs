namespace GameHubz.DataMigrations
{
    [Migration(9, "Alter tournament")]
    public class Migration_00009_Alter_Tournament : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("Tournament").AddColumn("Prize").AsInt32().NotNullable().WithDefaultValue(0);
            Alter.Table("Tournament").AddColumn("PrizeCurrency").AsInt32().NotNullable().WithDefaultValue(1);
            Alter.Table("Tournament").AddColumn("Region").AsInt32().NotNullable().WithDefaultValue(7);
        }
    }
}