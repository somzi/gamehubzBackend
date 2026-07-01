namespace GameHubz.DataMigrations
{
    [Migration(63, "Unique UserHub index — DB backstop against duplicate hub memberships (Follow race / double-tap)")]
    public class Migration_00063_Add_UserHub_Unique_Index : ForwardOnlyMigration
    {
        public override void Up()
        {
            // A user is a member of a given hub at most once. Duplicate active UserHub rows crashed
            // the member list on the client (duplicate keys) and inflated member counts. The app-level
            // guard in FollowHub / AddMember / ApproveRequest can't see another concurrent transaction's
            // uncommitted row, so this partial unique index closes the read-then-write race at the DB.
            // Soft-deleted rows are excluded so a member who left (or was removed/banned) can rejoin later.
            // UserId/HubId are nullable columns, hence the explicit NOT NULL guards.
            //
            // If this CREATE fails on existing data, the table still holds duplicate memberships from
            // before the fix — run the dedupe cleanup (keep one row per user+hub) before re-running.
            Execute.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""UX_UserHub_User_Hub""
                ON ""UserHub"" (""UserId"", ""HubId"")
                WHERE ""UserId"" IS NOT NULL AND ""HubId"" IS NOT NULL AND ""IsDeleted"" = FALSE;");
        }
    }
}
