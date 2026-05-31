namespace GameHubz.DataMigrations
{
    [Migration(41, "Add IsVerified to Hub and create HubVerificationRequest table")]
    public class Migration_00041_Add_HubVerification : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("Hub")
                .AddColumn("IsVerified").AsBoolean().NotNullable().WithDefaultValue(false);

            Create.Table("HubVerificationRequest")
                .WithColumn("Id").AsGuid().PrimaryKey()
                .WithColumn("HubId").AsGuid().NotNullable()
                .WithColumn("Reason").AsString(2000).NotNullable().WithDefaultValue(string.Empty)
                .WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("CreatedOn").AsDateTime().Nullable()
                .WithColumn("ModifiedOn").AsDateTime().Nullable()
                .WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("CreatedBy").AsGuid().Nullable()
                .WithColumn("ModifiedBy").AsGuid().Nullable();

            Create.ForeignKey("FK_HubVerificationRequest_Hub")
                .FromTable("HubVerificationRequest").ForeignColumn("HubId")
                .ToTable("Hub").PrimaryColumn("Id");

            Create.Index("IX_HubVerificationRequest_HubId_Status")
                .OnTable("HubVerificationRequest")
                .OnColumn("HubId").Ascending()
                .OnColumn("Status").Ascending();
        }
    }
}
