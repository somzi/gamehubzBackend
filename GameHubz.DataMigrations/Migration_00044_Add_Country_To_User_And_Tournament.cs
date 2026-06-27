namespace GameHubz.DataMigrations
{
    [Migration(44, "Add Country to User and Countries to Tournament")]
    public class Migration_00044_Add_Country_To_User_And_Tournament : ForwardOnlyMigration
    {
        public override void Up()
        {
            // User: single nullable ISO 3166-1 alpha-2 country code (null for all existing rows).
            // Once set it locks (API only allows null → value). Selecting a country derives Region.
            Alter.Table("User").AddColumn("Country").AsString(2).Nullable();

            // Tournament: zero or more ISO country codes as a Postgres text[] array.
            // Non-null/non-empty => country-scoped (visible only to users from one of those countries);
            // null => region-scoped (existing behavior). FluentMigrator has no native array type, so raw SQL.
            Execute.Sql("ALTER TABLE \"Tournament\" ADD COLUMN \"Countries\" text[] NULL;");
        }
    }
}
