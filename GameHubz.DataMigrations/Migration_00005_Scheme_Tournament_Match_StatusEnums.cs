namespace GameHubz.DataMigrations
{
    [Migration(5, "Update Tournament and Match status to enums")]
    public class Migration_00005_Scheme_Tournament_Match_StatusEnums : ForwardOnlyMigration
    {
        public override void Up()
        {
            Execute.Sql("ALTER TABLE \"Tournament\" ALTER COLUMN \"Status\" TYPE integer USING (\"Status\"::integer)");
            Execute.Sql("ALTER TABLE \"Match\" ALTER COLUMN \"Status\" TYPE integer USING (\"Status\"::integer)");

            // Zatim postaviš NotNullable i DefaultValue preko Fluent interfejsa
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