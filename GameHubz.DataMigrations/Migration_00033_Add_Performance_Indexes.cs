using FluentMigrator;

namespace GameHubz.DataMigrations
{
    [Migration(33, "Add performance indexes for hot query paths")]
    public class Migration_00033_Add_Performance_Indexes : ForwardOnlyMigration
    {
        public override void Up()
        {
            // ── Match table (heaviest queries in the app) ─────────────────

            // AreAllMatchesFinishedInTournament, GetByTournamentAndRound, GetByUser
            // Match.TournamentId has NO FK configured in EF → no index at all!
            Create.Index("IX_Match_TournamentId_Status")
                .OnTable("Match")
                .OnColumn("TournamentId").Ascending()
                .OnColumn("Status").Ascending();

            // GetByTournamentAndRound — bracket round queries
            Create.Index("IX_Match_TournamentId_RoundNumber")
                .OnTable("Match")
                .OnColumn("TournamentId").Ascending()
                .OnColumn("RoundNumber").Ascending();

            // GetLastMatchesByUserId, GetByUser — paginated match history (filter + sort)
            Create.Index("IX_Match_Status_ScheduledStartTime")
                .OnTable("Match")
                .OnColumn("Status").Ascending()
                .OnColumn("ScheduledStartTime").Descending();

            // ── Tournament table ──────────────────────────────────────────

            // GetNumberOfTournamentsWonByUserId — profile stats page
            // WinnerUserId has no FK relationship → no index, causes full table scan!
            Create.Index("IX_Tournament_WinnerUserId")
                .OnTable("Tournament")
                .OnColumn("WinnerUserId").Ascending();

            // Replace 2-column (HubId, Status) with 3-column version
            // to cover ORDER BY StartDate DESC in GetByHubPaged pagination
            Delete.Index("IX_Tournament_HubId_Status").OnTable("Tournament");
            Create.Index("IX_Tournament_HubId_Status_StartDate")
                .OnTable("Tournament")
                .OnColumn("HubId").Ascending()
                .OnColumn("Status").Ascending()
                .OnColumn("StartDate").Descending();

            // ── TournamentParticipant table ───────────────────────────────

            // GetUserByTournamentId, CheckIsUserIsRegistered — composite exact lookup
            // Individual indexes exist but composite is much faster for 2-column filter
            Create.Index("IX_TournamentParticipant_TournamentId_UserId")
                .OnTable("TournamentParticipant")
                .OnColumn("TournamentId").Ascending()
                .OnColumn("UserId").Ascending();

            // ── TournamentRegistration table ──────────────────────────────

            // GetPendingByTournamenId — WHERE TournamentId = X AND Status = Pending
            Create.Index("IX_TournamentRegistration_TournamentId_Status")
                .OnTable("TournamentRegistration")
                .OnColumn("TournamentId").Ascending()
                .OnColumn("Status").Ascending();

            // GetByTeamId — no FK or index exists at all!
            Create.Index("IX_TournamentRegistration_TeamId")
                .OnTable("TournamentRegistration")
                .OnColumn("TeamId").Ascending();

            // ── User table (auth hot path) ────────────────────────────────

            // GetByEmail, AnyByEmail, ShallowGetByEmail, IsEmailUnique
            // Called on EVERY login and registration — no index exists!
            Create.Index("IX_User_Email")
                .OnTable("User")
                .OnColumn("Email").Ascending();

            // ── RefreshToken table ────────────────────────────────────────

            // FindByTokenValue — token validation/refresh flow
            // Only FK index on UserId exists, Token column has no index
            Create.Index("IX_RefreshToken_Token")
                .OnTable("RefreshToken")
                .OnColumn("Token").Ascending();

            // ── UserHub table ─────────────────────────────────────────────

            // GetByUserAndHub — composite for exact user+hub pair lookups
            // Individual FK indexes exist but composite avoids double scan
            Create.Index("IX_UserHub_UserId_HubId")
                .OnTable("UserHub")
                .OnColumn("UserId").Ascending()
                .OnColumn("HubId").Ascending();
        }
    }
}
