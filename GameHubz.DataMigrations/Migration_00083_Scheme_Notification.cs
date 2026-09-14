using System.Data;

namespace GameHubz.DataMigrations
{
    [Migration(83, "Create Notification — per-user inbox of every notification GameHubz sends")]
    public class Migration_00083_Scheme_Notification : ForwardOnlyMigration
    {
        public override void Up()
        {
            Create.Table("Notification")
                .WithColumn("Id").AsGuid().PrimaryKey()
                .WithColumn("UserId").AsGuid().NotNullable()
                .WithColumn("Type").AsString(64).Nullable()
                .WithColumn("Category").AsInt32().NotNullable().WithDefaultValue(0)
                // Unsized text: the wording is a resource string plus names (tournament, hub, team,
                // user) whose length the server does not control, and one row tripping a varchar limit
                // would fail the insert for every recipient of that send.
                .WithColumn("Title").AsString().NotNullable()
                .WithColumn("Body").AsString().NotNullable()
                .WithColumn("DataJson").AsString().Nullable()
                .WithColumn("SeenOn").AsDateTime().Nullable()
                .WithColumn("ReadOn").AsDateTime().Nullable()
                .WithColumn("CreatedOn").AsDateTime().Nullable()
                .WithColumn("ModifiedOn").AsDateTime().Nullable()
                .WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("CreatedBy").AsGuid().Nullable()
                .WithColumn("ModifiedBy").AsGuid().Nullable();

            // An inbox is meaningless without its user, so a hard-deleted user takes it along.
            Create.ForeignKey("FK_Notification_User")
                .FromTable("Notification").ForeignColumn("UserId")
                .ToTable("User").PrimaryColumn("Id")
                .OnDelete(Rule.Cascade);

            // The inbox page: one user's rows, newest first, keyset on CreatedOn.
            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_Notification_User_CreatedOn""
                ON ""Notification"" (""UserId"", ""CreatedOn"" DESC);");

            // The counters (bell + tab badges), mark-seen and mark-all-read only ever touch unread rows.
            // Partial on ReadOn, so this holds just the unread tail however long the history grows.
            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_Notification_User_Unread""
                ON ""Notification"" (""UserId"", ""Category"")
                WHERE ""ReadOn"" IS NULL;");

            // The retention sweep deletes by age across every user.
            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_Notification_CreatedOn""
                ON ""Notification"" (""CreatedOn"");");
        }
    }
}
