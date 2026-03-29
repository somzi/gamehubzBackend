namespace GameHubz.DataMigrations
{
    [Migration(32, "Add TeamWinCondition to Tournament")]
    public class Migration_00032_Add_TeamWinCondition : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("Tournament")
                .AddColumn("TeamWinCondition").AsInt32().NotNullable().WithDefaultValue(0);
        }
    }
}