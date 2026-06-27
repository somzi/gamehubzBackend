using FluentMigrator;

namespace GameHubz.DataMigrations
{
    [Migration(61, "Add indexes for notification-badge organizer count queries")]
    public class Migration_00061_Add_Badge_Count_Indexes : ForwardOnlyMigration
    {
        public override void Up()
        {
            // BadgeService.ComputeAsync now runs a few extra COUNT queries to drive the new
            // organizer / captain badges. Most predicates are already index-backed:
            //   • TeamJoinRequest  → IX_TeamJoinRequest_TeamId_Status (migration 31)
            //   • UserHubRequest   → IX_UserHubRequest_HubId_Status   (migration 38)
            //   • TournamentRegistration → IX_TournamentRegistration_TournamentId_Status (migration 33)
            // The two below are the gaps that would otherwise scan.

            // TeamJoinRequestRepository.CountPendingForCaptain joins TeamJoinRequest → TournamentTeam
            // on CaptainUserId. The column is a FK to User but Postgres does not auto-index FK columns,
            // so the captain lookup had no supporting index.
            if (!Schema.Table("TournamentTeam").Index("IX_TournamentTeam_CaptainUserId").Exists())
            {
                Create.Index("IX_TournamentTeam_CaptainUserId")
                    .OnTable("TournamentTeam")
                    .OnColumn("CaptainUserId").Ascending();
            }

            // MatchRepository.CountAdminHelpForHubs counts open admin-help matches per tournament.
            // Composite (TournamentId, AdminHelpRequested) keeps the count index-only and lets the
            // rare AdminHelpRequested=true rows be found without scanning a tournament's full match set.
            if (!Schema.Table("Match").Index("IX_Match_TournamentId_AdminHelpRequested").Exists())
            {
                Create.Index("IX_Match_TournamentId_AdminHelpRequested")
                    .OnTable("Match")
                    .OnColumn("TournamentId").Ascending()
                    .OnColumn("AdminHelpRequested").Ascending();
            }
        }
    }
}
