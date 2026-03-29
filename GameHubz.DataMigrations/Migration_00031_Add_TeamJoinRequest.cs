namespace GameHubz.DataMigrations
{
    [Migration(31, "Add TeamJoinRequest table and RequiresApproval to TournamentTeam")]
    public class Migration_00031_Add_TeamJoinRequest : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("TournamentTeam")
                .AddColumn("RequiresApproval").AsBoolean().NotNullable().WithDefaultValue(false);

            Create.Table("TeamJoinRequest")
                .WithColumn("Id").AsGuid().PrimaryKey()
                .WithColumn("TeamId").AsGuid().NotNullable()
                .WithColumn("UserId").AsGuid().NotNullable()
                .WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("CreatedOn").AsDateTime().Nullable()
                .WithColumn("ModifiedOn").AsDateTime().Nullable()
                .WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("CreatedBy").AsGuid().Nullable()
                .WithColumn("ModifiedBy").AsGuid().Nullable();

            Create.ForeignKey("FK_TeamJoinRequest_Team")
                .FromTable("TeamJoinRequest").ForeignColumn("TeamId")
                .ToTable("TournamentTeam").PrimaryColumn("Id");

            Create.ForeignKey("FK_TeamJoinRequest_User")
                .FromTable("TeamJoinRequest").ForeignColumn("UserId")
                .ToTable("User").PrimaryColumn("Id");

            Create.Index("IX_TeamJoinRequest_TeamId_Status")
                .OnTable("TeamJoinRequest")
                .OnColumn("TeamId").Ascending()
                .OnColumn("Status").Ascending();
        }
    }
}
