using System.Data;

namespace GameHubz.DataMigrations
{
    [Migration(8, "Create TournamentStage, TournamentGroup, TournamentParticipant")]
    public class Migration_00008_Scheme_TournamentStage_TournamentGroup_TournamentParticipant : ForwardOnlyMigration
    {
        public override void Up()
        {
            this.Create.TableWithCommonColumns("TournamentStage")
                .WithColumn("Type").AsInt32().NotNullable()
                .WithColumn("Order").AsInt32().NotNullable()
                .WithColumn("QualifiedPlayersCount").AsInt32().Nullable()
                .WithColumn("TournamentId").AsGuid().Nullable()
                    .ForeignKey("Tournament", "Id").OnDeleteOrUpdate(Rule.None);

            this.Create.TableWithCommonColumns("TournamentGroup")
                .WithColumn("Name").AsString().NotNullable()
                .WithColumn("TournamentStageId").AsGuid().Nullable()
                    .ForeignKey("TournamentStage", "Id").OnDeleteOrUpdate(Rule.None);

            this.Create.TableWithCommonColumns("TournamentParticipant")
                .WithColumn("TournamentId").AsGuid().Nullable()
                    .ForeignKey("Tournament", "Id").OnDeleteOrUpdate(Rule.None)
                .WithColumn("UserId").AsGuid().Nullable()
                    .ForeignKey("User", "Id").OnDeleteOrUpdate(Rule.None)
                .WithColumn("Seed").AsInt32().Nullable()
                .WithColumn("GroupPosition").AsInt32().Nullable()
                .WithColumn("Points").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Wins").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Losses").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Draws").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("GoalsFor").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("GoalsAgainst").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("TournamentGroupId").AsGuid().Nullable()
                    .ForeignKey("TournamentGroup", "Id").OnDeleteOrUpdate(Rule.None);

            this.Alter.Table("Match")
                .AddColumn("TournamentStageId").AsGuid().Nullable()
                    .ForeignKey("TournamentStage", "Id").OnDeleteOrUpdate(Rule.None)
                .AddColumn("Stage").AsInt32().NotNullable().WithDefaultValue(1)
                .AddColumn("TournamentGroupId").AsGuid().Nullable()
                    .ForeignKey("TournamentGroup", "Id").OnDeleteOrUpdate(Rule.None)
                .AddColumn("MatchOrder").AsInt32().Nullable()
                .AddColumn("NextMatchId").AsGuid().Nullable()
                    .ForeignKey("Match", "Id").OnDeleteOrUpdate(Rule.None)
                .AddColumn("NextMatchLoserBracketId").AsGuid().Nullable()
                    .ForeignKey("Match", "Id").OnDeleteOrUpdate(Rule.None)
                .AddColumn("IsUpperBracket").AsBoolean().NotNullable().WithDefaultValue(true);

            this.Delete.Column("MaxPlayers").FromTable("Tournament");
            Alter.Table("Tournament").AddColumn("MaxPlayers").AsInt32().NotNullable();
            Alter.Table("Tournament").AddColumn("Format").AsInt32().NotNullable();

            this.Alter.Table("Match")
                .AddColumn("HomeParticipantId").AsGuid().Nullable()
                    .ForeignKey("TournamentParticipant", "Id").OnDeleteOrUpdate(Rule.None)
                .AddColumn("WinnerParticipantId").AsGuid().Nullable()
                    .ForeignKey("TournamentParticipant", "Id").OnDeleteOrUpdate(Rule.None)
                .AddColumn("AwayParticipantId").AsGuid().Nullable()
                    .ForeignKey("TournamentParticipant", "Id").OnDeleteOrUpdate(Rule.None);

            this.Delete.ForeignKey("FK_Match_HomeUserId_User_Id").OnTable("Match");
            this.Delete.ForeignKey("FK_Match_AwayUserId_User_Id").OnTable("Match");
            this.Delete.ForeignKey("FK_Match_WinnerUserId_User_Id").OnTable("Match");
            this.Delete.Column("HomeUserId").FromTable("Match");
            this.Delete.Column("AwayUserId").FromTable("Match");
            this.Delete.Column("WinnerUserId").FromTable("Match");

            Alter.Table("Tournament").AddColumn("Format").AsInt32().NotNullable().WithDefaultValue(3);
        }
    }
}