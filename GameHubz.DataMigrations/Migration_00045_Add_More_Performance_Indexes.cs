namespace GameHubz.DataMigrations
{
    [Migration(45, "Add indexes for TournamentTeamMember.UserId, User auth tokens, and Hub lookup columns")]
    public class Migration_00045_Add_More_Performance_Indexes : ForwardOnlyMigration
    {
        public override void Up()
        {
            // TournamentTeamMember.UserId — TournamentTeamMemberRepository.GetByUserId
            // and ExistsInTournament (called on every team join attempt).
            // Only IX_TournamentTeamMember_TeamId exists; UserId column had no index.
            if (!Schema.Table("TournamentTeamMember").Index("IX_TournamentTeamMember_UserId").Exists())
                Create.Index("IX_TournamentTeamMember_UserId")
                    .OnTable("TournamentTeamMember")
                    .OnColumn("UserId").Ascending();

            // User.VerifyEmailToken — UserRepository.GetByVerifyEmailToken.
            // Token-based lookup hit during email verification flow.
            if (!Schema.Table("User").Index("IX_User_VerifyEmailToken").Exists())
                Create.Index("IX_User_VerifyEmailToken")
                    .OnTable("User")
                    .OnColumn("VerifyEmailToken").Ascending();

            // User.ForgotPasswordToken — UserRepository.GetByForgotPasswordToken.
            // Token-based lookup hit during password reset flow.
            if (!Schema.Table("User").Index("IX_User_ForgotPasswordToken").Exists())
                Create.Index("IX_User_ForgotPasswordToken")
                    .OnTable("User")
                    .OnColumn("ForgotPasswordToken").Ascending();

            // Hub.Name — HubRepository.GetHubsByUserId uses Name.StartsWith(search)
            // for discovery. A B-tree index handles LIKE 'prefix%' on PostgreSQL.
            if (!Schema.Table("Hub").Index("IX_Hub_Name").Exists())
                Create.Index("IX_Hub_Name")
                    .OnTable("Hub")
                    .OnColumn("Name").Ascending();

            // Hub.UserId — HubRepository.GetByUserId, UserOwnsAnyHub.
            // FK exists but no explicit index was created.
            if (!Schema.Table("Hub").Index("IX_Hub_UserId").Exists())
                Create.Index("IX_Hub_UserId")
                    .OnTable("Hub")
                    .OnColumn("UserId").Ascending();
        }
    }
}