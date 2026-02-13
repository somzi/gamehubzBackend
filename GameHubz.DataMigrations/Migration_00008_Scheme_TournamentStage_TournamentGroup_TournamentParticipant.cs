using System.Data;

namespace GameHubz.DataMigrations
{
    [Migration(8, "Create TournamentStage, TournamentGroup, TournamentParticipant")]
    public class Migration_00008_Scheme_TournamentStage_TournamentGroup_TournamentParticipant : ForwardOnlyMigration
    {
        public override void Up()
        {
            // 1. Postojeće tabele koje kreiraš (TournamentStage, Group, Participant)
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

            // 2. Popravka za Status (Postgres fix)
            Execute.Sql("ALTER TABLE \"Tournament\" ALTER COLUMN \"Status\" TYPE integer USING (\"Status\"::integer)");
            Execute.Sql("ALTER TABLE \"Match\" ALTER COLUMN \"Status\" TYPE integer USING (\"Status\"::integer)");

            Alter.Column("Status").OnTable("Tournament").AsInt32().NotNullable().WithDefaultValue(1);
            Alter.Column("Status").OnTable("Match").AsInt32().NotNullable().WithDefaultValue(1);

            // 3. Izmene na Match tabeli
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
                .AddColumn("IsUpperBracket").AsBoolean().NotNullable().WithDefaultValue(true)
                .AddColumn("HomeParticipantId").AsGuid().Nullable()
                    .ForeignKey("TournamentParticipant", "Id").OnDeleteOrUpdate(Rule.None)
                .AddColumn("WinnerParticipantId").AsGuid().Nullable()
                    .ForeignKey("TournamentParticipant", "Id").OnDeleteOrUpdate(Rule.None)
                .AddColumn("AwayParticipantId").AsGuid().Nullable()
                    .ForeignKey("TournamentParticipant", "Id").OnDeleteOrUpdate(Rule.None);

            // 4. Brisanje starih kolona na Match (ako postoje)
            this.Delete.ForeignKey("FK_Match_HomeUserId_User_Id").OnTable("Match");
            this.Delete.ForeignKey("FK_Match_AwayUserId_User_Id").OnTable("Match");
            this.Delete.ForeignKey("FK_Match_WinnerUserId_User_Id").OnTable("Match");
            this.Delete.Column("HomeUserId").FromTable("Match");
            this.Delete.Column("AwayUserId").FromTable("Match");
            this.Delete.Column("WinnerUserId").FromTable("Match");

            // 5. Izmene na Tournament tabeli (BEZ DUPLIRANJA)
            if (Schema.Table("Tournament").Column("MaxPlayers").Exists())
                this.Delete.Column("MaxPlayers").FromTable("Tournament");

            this.Alter.Table("Tournament")
                .AddColumn("MaxPlayers").AsInt32().NotNullable().WithDefaultValue(0)
                .AddColumn("Format").AsInt32().NotNullable().WithDefaultValue(3);
        }
    }
}