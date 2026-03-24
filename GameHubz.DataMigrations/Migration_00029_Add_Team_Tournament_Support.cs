namespace GameHubz.DataMigrations
{
    [Migration(29, "Add Team Tournament Support")]
    public class Migration_00029_Add_Team_Tournament_Support : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Tournament: add team columns
            Alter.Table("Tournament").AddColumn("IsTeamTournament").AsBoolean().NotNullable().WithDefaultValue(false);
            Alter.Table("Tournament").AddColumn("TeamSize").AsInt32().Nullable();
            Alter.Table("Tournament").AddColumn("WinnerTeamId").AsGuid().Nullable();

            // TournamentTeam table
            Create.TableWithCommonColumns("TournamentTeam")
                .WithColumn("TournamentId").AsGuid().Nullable().ForeignKey("Tournament", "Id")
                .WithColumn("TeamName").AsString(256).NotNullable()
                .WithColumn("CaptainUserId").AsGuid().Nullable().ForeignKey("User", "Id")
                .WithColumn("TournamentParticipantId").AsGuid().Nullable();

            // TournamentTeamMember table
            Create.TableWithCommonColumns("TournamentTeamMember")
                .WithColumn("TeamId").AsGuid().Nullable().ForeignKey("TournamentTeam", "Id")
                .WithColumn("UserId").AsGuid().Nullable().ForeignKey("User", "Id")
                .WithColumn("JoinedAt").AsDateTime().Nullable();

            // TeamMatch table
            Create.TableWithCommonColumns("TeamMatch")
                .WithColumn("TournamentId").AsGuid().NotNullable().ForeignKey("Tournament", "Id")
                .WithColumn("TournamentStageId").AsGuid().Nullable().ForeignKey("TournamentStage", "Id")
                .WithColumn("HomeTeamParticipantId").AsGuid().Nullable().ForeignKey("TournamentParticipant", "Id")
                .WithColumn("AwayTeamParticipantId").AsGuid().Nullable().ForeignKey("TournamentParticipant", "Id")
                .WithColumn("HomeTeamRepresentativeUserId").AsGuid().Nullable()
                .WithColumn("AwayTeamRepresentativeUserId").AsGuid().Nullable()
                .WithColumn("RoundNumber").AsInt32().Nullable()
                .WithColumn("MatchOrder").AsInt32().Nullable()
                .WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(1)
                .WithColumn("WinnerTeamParticipantId").AsGuid().Nullable()
                .WithColumn("NextTeamMatchId").AsGuid().Nullable();

            // Match: add TeamMatchId
            Alter.Table("Match").AddColumn("TeamMatchId").AsGuid().Nullable().ForeignKey("TeamMatch", "Id");

            // TournamentParticipant: add TeamId
            Alter.Table("TournamentParticipant").AddColumn("TeamId").AsGuid().Nullable();

            // TournamentRegistration: add TeamId
            Alter.Table("TournamentRegistration").AddColumn("TeamId").AsGuid().Nullable();

            // Indexes
            Create.Index("IX_TeamMatch_TournamentStageId").OnTable("TeamMatch").OnColumn("TournamentStageId");
            Create.Index("IX_TournamentTeam_TournamentId").OnTable("TournamentTeam").OnColumn("TournamentId");
            Create.Index("IX_TournamentTeamMember_TeamId").OnTable("TournamentTeamMember").OnColumn("TeamId");
        }
    }
}