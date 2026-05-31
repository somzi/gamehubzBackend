namespace GameHubz.DataMigrations
{
    [Migration(39, "Add HubRole column to UserHub and backfill hub owners as UserHub rows with HubRole=Owner")]
    public class Migration_00039_Add_HubRole_To_UserHub : ForwardOnlyMigration
    {
        // Mirrors GameHubz.DataModels.Enums.HubRole — keep in sync.
        private const int RoleHubOwner = 1;

        private const int RoleHubMember = 3;

        public override void Up()
        {
            Alter.Table("UserHub")
                .AddColumn("HubRole").AsInt32().NotNullable().WithDefaultValue(RoleHubMember);

            // Backfill: insert a UserHub row for each Hub owner that doesn't already have one,
            // so membership lookups can use UserHub uniformly while Hub.UserId stays canonical.
            Execute.Sql($@"
                INSERT INTO ""UserHub"" (""Id"", ""UserId"", ""HubId"", ""HubRole"", ""CreatedOn"", ""ModifiedOn"", ""CreatedBy"", ""ModifiedBy"", ""IsDeleted"")
                SELECT
                    gen_random_uuid(),
                    h.""UserId"",
                    h.""Id"",
                    {RoleHubOwner},
                    NOW() AT TIME ZONE 'UTC',
                    NOW() AT TIME ZONE 'UTC',
                    h.""UserId"",
                    h.""UserId"",
                    FALSE
                FROM ""Hub"" h
                WHERE h.""IsDeleted"" = FALSE
                  AND NOT EXISTS (
                      SELECT 1 FROM ""UserHub"" uh
                      WHERE uh.""UserId"" = h.""UserId""
                        AND uh.""HubId"" = h.""Id""
                        AND uh.""IsDeleted"" = FALSE
                  );
            ");

            // Promote any pre-existing UserHub rows that belong to the hub owner to Owner role.
            Execute.Sql($@"
                UPDATE ""UserHub"" uh
                SET ""HubRole"" = {RoleHubOwner},
                    ""ModifiedOn"" = NOW() AT TIME ZONE 'UTC'
                FROM ""Hub"" h
                WHERE uh.""HubId"" = h.""Id""
                  AND uh.""UserId"" = h.""UserId""
                  AND uh.""IsDeleted"" = FALSE
                  AND h.""IsDeleted"" = FALSE
                  AND uh.""HubRole"" <> {RoleHubOwner};
            ");
        }
    }
}