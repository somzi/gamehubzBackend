namespace GameHubz.DataMigrations
{
    [Migration(87, "Add indexes for hub list pagination, membership counts and prefix search")]
    public class Migration_00087_Add_Hub_List_Indexes : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Joined/discovery pages sort live hubs by name and id. The older Name-only
            // index cannot cover the tie-breaker and also includes deleted hubs.
            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_Hub_Live_Name_Id""
                    ON ""Hub"" (""Name"", ""Id"")
                    WHERE ""IsDeleted"" = FALSE;

                -- Hub list cards count active members per hub. The existing UserId,HubId
                -- index starts with the wrong column for this correlated count.
                CREATE INDEX IF NOT EXISTS ""IX_UserHub_Live_HubId""
                    ON ""UserHub"" (""HubId"")
                    WHERE ""IsDeleted"" = FALSE;

                -- The case-insensitive prefix filter uses lower(Name), which cannot use
                -- the older plain Name index. text_pattern_ops supports LIKE 'prefix%'
                -- regardless of the database collation.
                CREATE INDEX IF NOT EXISTS ""IX_Hub_Live_LowerName_Prefix""
                    ON ""Hub"" (lower(""Name"") text_pattern_ops)
                    WHERE ""IsDeleted"" = FALSE;");
        }
    }
}
