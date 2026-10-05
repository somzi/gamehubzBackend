using System.Data;

namespace GameHubz.DataMigrations
{
    [Migration(96, "Bind verification phones to tournament players and review changes")]
    public class Migration_00096_Add_Tournament_Player_Device : ForwardOnlyMigration
    {
        public override void Up()
        {
            Create.TableWithCommonColumns("TournamentPlayerDevice")
                .WithColumn("TournamentId").AsGuid().NotNullable()
                .WithColumn("UserId").AsGuid().NotNullable()
                .WithColumn("UserDeviceId").AsGuid().NotNullable()
                .WithColumn("Status").AsInt32().NotNullable()
                .WithColumn("RequestedOn").AsDateTime().NotNullable()
                .WithColumn("DecidedOn").AsDateTime().Nullable()
                .WithColumn("DecidedByUserId").AsGuid().Nullable();

            Create.ForeignKey("FK_TournamentPlayerDevice_Tournament")
                .FromTable("TournamentPlayerDevice").ForeignColumn("TournamentId")
                .ToTable("Tournament").PrimaryColumn("Id").OnDelete(Rule.Cascade);
            Create.ForeignKey("FK_TournamentPlayerDevice_User")
                .FromTable("TournamentPlayerDevice").ForeignColumn("UserId")
                .ToTable("User").PrimaryColumn("Id").OnDelete(Rule.Cascade);
            Create.ForeignKey("FK_TournamentPlayerDevice_UserDevice")
                .FromTable("TournamentPlayerDevice").ForeignColumn("UserDeviceId")
                .ToTable("UserDevice").PrimaryColumn("Id").OnDelete(Rule.Cascade);

            Execute.Sql(@"
                CREATE UNIQUE INDEX ""UX_TournamentPlayerDevice_Active""
                ON ""TournamentPlayerDevice"" (""TournamentId"", ""UserId"")
                WHERE ""Status"" = 1 AND ""IsDeleted"" = false;
                CREATE UNIQUE INDEX ""UX_TournamentPlayerDevice_Pending""
                ON ""TournamentPlayerDevice"" (""TournamentId"", ""UserId"")
                WHERE ""Status"" = 2 AND ""IsDeleted"" = false;
                CREATE INDEX ""IX_TournamentPlayerDevice_UserDevice""
                ON ""TournamentPlayerDevice"" (""UserDeviceId"");");
        }
    }
}
