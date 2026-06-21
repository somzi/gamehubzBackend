namespace GameHubz.DataMigrations
{
    [Migration(56, "Create ErrorLog — persists server-side errors for triage")]
    public class Migration_00056_Scheme_ErrorLog : ForwardOnlyMigration
    {
        public override void Up()
        {
            this.Create.TableWithCommonColumns("ErrorLog")
                .WithColumn("UserId").AsGuid().Nullable()
                .WithColumn("Category").AsString(64).NotNullable().WithDefaultValue("")
                .WithColumn("ExceptionType").AsString(256).NotNullable().WithDefaultValue("")
                .WithColumn("Message").AsMaxString().NotNullable().WithDefaultValue("")
                .WithColumn("StackTrace").AsMaxString().Nullable()
                .WithColumn("Source").AsString(512).Nullable()
                .WithColumn("StatusCode").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("RequestMethod").AsString(16).NotNullable().WithDefaultValue("")
                .WithColumn("RequestPath").AsString(1024).NotNullable().WithDefaultValue("")
                .WithColumn("QueryString").AsString(2048).Nullable()
                .WithColumn("RequestBody").AsMaxString().Nullable()
                .WithColumn("UserAgent").AsString(1024).Nullable()
                .WithColumn("AppVersion").AsString(64).Nullable()
                .WithColumn("Platform").AsString(64).Nullable()
                .WithColumn("IpAddress").AsString(64).Nullable()
                .WithColumn("IsResolved").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("ResolutionNotes").AsMaxString().Nullable();

            // Triage view: newest unresolved errors first.
            this.Create.Index("IX_ErrorLog_IsResolved_CreatedOn")
                .OnTable("ErrorLog")
                .OnColumn("IsResolved").Ascending()
                .OnColumn("CreatedOn").Descending();

            this.Create.Index("IX_ErrorLog_UserId")
                .OnTable("ErrorLog")
                .OnColumn("UserId").Ascending();
        }
    }
}
