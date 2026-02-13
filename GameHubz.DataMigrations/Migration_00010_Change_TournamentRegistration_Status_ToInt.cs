using System.Data;

namespace GameHubz.DataMigrations
{
    [Migration(10, "Change TournamentRegistration.Status from string to int")]
    public class Migration_00010_Change_TournamentRegistration_Status_ToInt : ForwardOnlyMigration
    {
        public override void Up()
        {
            Execute.Sql("ALTER TABLE \"TournamentRegistration\" ALTER COLUMN \"Status\" TYPE integer USING (\"Status\"::integer)");

            // 2. Sada FluentMigrator može da preuzme postavljanje NOT NULL i ostalih pravila
            this.Alter.Table("TournamentRegistration")
                .AlterColumn("Status")
                .AsInt32()
                .NotNullable();
        }
    }
}