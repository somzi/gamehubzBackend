namespace GameHubz.DataMigrations
{
    [Migration(52, "Create ShareLog — tracks opens of public share links")]
    public class Migration_00052_Scheme_ShareLog : ForwardOnlyMigration
    {
        public override void Up()
        {
            this.Create.TableWithCommonColumns("ShareLog")
                .WithColumn("EntityId").AsGuid().NotNullable()
                .WithColumn("EntityType").AsInt32().NotNullable()
                .WithColumn("Platform").AsString(64).Nullable()
                .WithColumn("IpAddress").AsString(64).Nullable()
                .WithColumn("UserAgent").AsString(512).Nullable();

            this.Create.Index("IX_ShareLog_EntityType_EntityId")
                .OnTable("ShareLog")
                .OnColumn("EntityType").Ascending()
                .OnColumn("EntityId").Ascending();
        }
    }
}
