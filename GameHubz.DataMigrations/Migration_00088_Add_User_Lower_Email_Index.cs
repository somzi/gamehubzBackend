namespace GameHubz.DataMigrations
{
    [Migration(88, "Expression index on lower(Email) for the case-insensitive e-mail lookups")]
    public class Migration_00088_Add_User_Lower_Email_Index : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Every e-mail lookup in UserRepository (login, registration uniqueness, password reset)
            // compares lower("Email"), which IX_User_Email on the raw column cannot serve, so each
            // one read the whole User table. Deliberately not unique: e-mail uniqueness has only ever
            // been checked in the app, so duplicates may already exist and would fail a unique index.
            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_User_LowerEmail""
                    ON ""User"" (lower(""Email""));");
        }
    }
}
