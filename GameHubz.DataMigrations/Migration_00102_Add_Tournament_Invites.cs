using System.Data;

namespace GameHubz.DataMigrations
{
    [Migration(102, "Invite-only tournaments and tournament invites (entry + participant swap)")]
    public class Migration_00102_Add_Tournament_Invites : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Third registration mode next to open and code-locked (IsPrivate). False on every
            // existing tournament: they keep taking sign-ups exactly as before.
            Alter.Table("Tournament")
                .AddColumn("IsInviteOnly").AsBoolean().NotNullable().WithDefaultValue(false);

            Create.TableWithCommonColumns("TournamentInvite")
                .WithColumn("TournamentId").AsGuid().NotNullable()
                .WithColumn("UserId").AsGuid().NotNullable()
                .WithColumn("InvitedByUserId").AsGuid().Nullable()
                // TournamentInviteKind: 1 = Entry, 2 = Swap
                .WithColumn("Kind").AsInt32().NotNullable()
                // TournamentInviteStatus: 1 = Pending, 2 = Accepted, 3 = Declined, 4 = Cancelled, 5 = Expired
                .WithColumn("Status").AsInt32().NotNullable()
                .WithColumn("IsGuest").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("SwapOutgoingUserId").AsGuid().Nullable()
                .WithColumn("RespondedOn").AsDateTime().Nullable();

            Create.ForeignKey("FK_TournamentInvite_Tournament")
                .FromTable("TournamentInvite").ForeignColumn("TournamentId")
                .ToTable("Tournament").PrimaryColumn("Id").OnDelete(Rule.Cascade);

            Create.ForeignKey("FK_TournamentInvite_User")
                .FromTable("TournamentInvite").ForeignColumn("UserId")
                .ToTable("User").PrimaryColumn("Id").OnDelete(Rule.Cascade);

            // One open invite per player per tournament — the backstop for two managers inviting the
            // same player at once. Answered rows stay (a decline is what blocks a second invite), so
            // they are outside the index.
            Execute.Sql(@"
                CREATE UNIQUE INDEX ""UX_TournamentInvite_Pending""
                ON ""TournamentInvite"" (""TournamentId"", ""UserId"")
                WHERE ""Status"" = 1 AND ""IsDeleted"" = false;
                CREATE INDEX ""IX_TournamentInvite_Tournament_User""
                ON ""TournamentInvite"" (""TournamentId"", ""UserId"");
                CREATE INDEX ""IX_TournamentInvite_User_Status""
                ON ""TournamentInvite"" (""UserId"", ""Status"");");
        }
    }
}
