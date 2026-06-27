namespace GameHubz.DataMigrations
{
    [Migration(40, "Create UserHubBan table for hub bans")]
    public class Migration_00040_Add_UserHubBan : ForwardOnlyMigration
    {
        public override void Up()
        {
            Create.Table("UserHubBan")
                .WithColumn("Id").AsGuid().PrimaryKey()
                .WithColumn("UserId").AsGuid().NotNullable()
                .WithColumn("HubId").AsGuid().NotNullable()
                .WithColumn("BannedById").AsGuid().Nullable()
                .WithColumn("CreatedOn").AsDateTime().Nullable()
                .WithColumn("ModifiedOn").AsDateTime().Nullable()
                .WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("CreatedBy").AsGuid().Nullable()
                .WithColumn("ModifiedBy").AsGuid().Nullable();

            Create.ForeignKey("FK_UserHubBan_Hub")
                .FromTable("UserHubBan").ForeignColumn("HubId")
                .ToTable("Hub").PrimaryColumn("Id");

            Create.ForeignKey("FK_UserHubBan_User")
                .FromTable("UserHubBan").ForeignColumn("UserId")
                .ToTable("User").PrimaryColumn("Id");

            Create.Index("IX_UserHubBan_HubId_UserId")
                .OnTable("UserHubBan")
                .OnColumn("HubId").Ascending()
                .OnColumn("UserId").Ascending();
        }
    }
}
