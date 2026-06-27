namespace GameHubz.DataMigrations
{
    [Migration(38, "Add IsPublic to Hub and create UserHubRequest table")]
    public class Migration_00038_Add_HubPrivacy_And_UserHubRequest : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("Hub")
                .AddColumn("IsPublic").AsBoolean().NotNullable().WithDefaultValue(true);

            Create.Table("UserHubRequest")
                .WithColumn("Id").AsGuid().PrimaryKey()
                .WithColumn("HubId").AsGuid().NotNullable()
                .WithColumn("UserId").AsGuid().NotNullable()
                .WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("CreatedOn").AsDateTime().Nullable()
                .WithColumn("ModifiedOn").AsDateTime().Nullable()
                .WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("CreatedBy").AsGuid().Nullable()
                .WithColumn("ModifiedBy").AsGuid().Nullable();

            Create.ForeignKey("FK_UserHubRequest_Hub")
                .FromTable("UserHubRequest").ForeignColumn("HubId")
                .ToTable("Hub").PrimaryColumn("Id");

            Create.ForeignKey("FK_UserHubRequest_User")
                .FromTable("UserHubRequest").ForeignColumn("UserId")
                .ToTable("User").PrimaryColumn("Id");

            Create.Index("IX_UserHubRequest_HubId_Status")
                .OnTable("UserHubRequest")
                .OnColumn("HubId").Ascending()
                .OnColumn("Status").Ascending();
        }
    }
}