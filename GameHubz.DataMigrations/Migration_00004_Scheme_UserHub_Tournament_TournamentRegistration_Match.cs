using System.Data;

namespace GameHubz.DataMigrations
{
    [Migration(4, "Create UserHub, Tournament, TournamentRegistration, Match")]
    public class Migration_00004_Scheme_UserHub_Tournament_TournamentRegistration_Match : ForwardOnlyMigration
    {
        public override void Up()
        {
            this.Create.TableWithCommonColumns("UserHub")
             .WithColumn("UserId").AsGuid().Nullable()
                 .ForeignKey("User", "Id").OnDeleteOrUpdate(Rule.None)
             .WithColumn("HubId").AsGuid().Nullable()
                 .ForeignKey("Hub", "Id").OnDeleteOrUpdate(Rule.None);

            this.Create.TableWithCommonColumns("Tournament")
            .WithColumn("HubId").AsGuid().Nullable()
                .ForeignKey("Hub", "Id").OnDeleteOrUpdate(Rule.None)
            .WithColumn("Name").AsString(200).NotNullable()
            .WithColumn("Description").AsString().Nullable()
            .WithColumn("Rules").AsString().Nullable()
            .WithColumn("Status").AsString().Nullable()
            .WithColumn("MaxPlayers").AsString().Nullable()
            .WithColumn("StartDate").AsDateTime().Nullable()
            .WithColumn("RegistrationDeadline").AsDateTime().Nullable();

            this.Create.TableWithCommonColumns("TournamentRegistration")
            .WithColumn("UserId").AsGuid().Nullable()
                .ForeignKey("User", "Id").OnDeleteOrUpdate(Rule.None)
            .WithColumn("Status").AsString().Nullable()
            .WithColumn("TournamentId").AsGuid().Nullable()
                .ForeignKey("Tournament", "Id").OnDeleteOrUpdate(Rule.None);

            this.Create.TableWithCommonColumns("Match")
             .WithColumn("TournamentId").AsGuid().Nullable()
                .ForeignKey("Tournament", "Id").OnDeleteOrUpdate(Rule.None)
            .WithColumn("RoundNumber").AsInt32().Nullable()
            .WithColumn("HomeUserId").AsGuid().NotNullable()
                .ForeignKey("User", "Id").OnDeleteOrUpdate(Rule.None)
            .WithColumn("AwayUserId").AsGuid().NotNullable()
                .ForeignKey("User", "Id").OnDeleteOrUpdate(Rule.None)
            .WithColumn("HomeUserScore").AsInt32().Nullable()
            .WithColumn("AwayUserScore").AsInt32().Nullable()
            .WithColumn("ScheduledStartTime").AsDateTime().Nullable()
            .WithColumn("Status").AsString().Nullable()
            .WithColumn("WinnerUserId").AsGuid().Nullable()
                .ForeignKey("User", "Id").OnDeleteOrUpdate(Rule.None);
        }
    }
}