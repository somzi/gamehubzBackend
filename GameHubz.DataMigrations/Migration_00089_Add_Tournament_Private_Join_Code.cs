namespace GameHubz.DataMigrations
{
    [Migration(89, "Add IsPrivate + JoinCode to Tournament (invite-only tournaments)")]
    public class Migration_00089_Add_Tournament_Private_Join_Code : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Private = hidden from every listing and announcement; players get in through the
            // share link or the six-digit JoinCode. False for every existing tournament.
            Alter.Table("Tournament").AddColumn("IsPrivate").AsBoolean().NotNullable().WithDefaultValue(false);
            Alter.Table("Tournament").AddColumn("JoinCode").AsString(6).Nullable();

            // The code is looked up on every "join with code" attempt and must resolve to exactly
            // one tournament. Unique over soft-deleted rows too, because the generator checks
            // against the whole table and a code must never be handed out twice.
            Execute.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Tournament_JoinCode""
                ON ""Tournament"" (""JoinCode"")
                WHERE ""JoinCode"" IS NOT NULL;");
        }
    }
}
