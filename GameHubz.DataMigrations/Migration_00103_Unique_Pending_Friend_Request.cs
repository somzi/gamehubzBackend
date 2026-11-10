namespace GameHubz.DataMigrations
{
    [Migration(103, "One pending friend request per unordered user pair")]
    public class Migration_00103_Unique_Pending_Friend_Request : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Preserve the oldest pending request and its direction. Historical duplicates remain
            // as cancelled rows, so no request history is deleted when installing the constraint.
            Execute.Sql("""
                WITH ranked AS (
                    SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY LEAST("FromUserId", "ToUserId"), GREATEST("FromUserId", "ToUserId")
                        ORDER BY "CreatedOn" ASC NULLS LAST, "Id"
                    ) AS position
                    FROM "FriendRequest"
                    WHERE "Status" = 0 AND "IsDeleted" = false
                )
                UPDATE "FriendRequest" SET "Status" = 3, "ModifiedOn" = CURRENT_TIMESTAMP
                WHERE "Id" IN (SELECT "Id" FROM ranked WHERE position > 1);

                CREATE UNIQUE INDEX "UX_FriendRequest_PendingPair"
                ON "FriendRequest" (LEAST("FromUserId", "ToUserId"), GREATEST("FromUserId", "ToUserId"))
                WHERE "Status" = 0 AND "IsDeleted" = false;
                """);
        }
    }
}
