namespace GameHubz.DataMigrations
{
    [Migration(90, "Index team sub-match lookup on Match.TeamMatchId")]
    public class Migration_00090_Add_Match_TeamMatchId_Index : ForwardOnlyMigration
    {
        public override void Up()
        {
            // SubMatches are loaded by TeamMatchId. Solo matches have no parent team match
            // and do not need an index entry. Include soft-deleted rows for FK checks too.
            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_Match_TeamMatchId""
                    ON ""Match"" (""TeamMatchId"")
                    WHERE ""TeamMatchId"" IS NOT NULL;");
        }
    }
}
