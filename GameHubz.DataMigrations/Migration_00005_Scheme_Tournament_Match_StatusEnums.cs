namespace GameHubz.DataMigrations
{
    [Migration(5, "Update Tournament and Match status to enums")]
    public class Migration_00005_Scheme_Tournament_Match_StatusEnums : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Column("Status")
                .OnTable("Tournament")
                .AsInt32()
                .NotNullable()
                .WithDefaultValue(1);

            Alter.Column("Status")
                .OnTable("Match")
                .AsInt32()
                .NotNullable()
                .WithDefaultValue(1);
        }
    }
}