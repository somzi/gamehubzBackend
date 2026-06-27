namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// Aggregate unread / pending counters for the signed-in user, used to drive the
    /// notification badges on the bottom-tab navigation (Social + Home) and the
    /// sub-tabs (Requests / Chats). Returned by GET api/v2/badges and pushed live
    /// through the UserHub ("BadgesUpdated") whenever one of the underlying counts changes.
    /// </summary>
    public class BadgeCountsDto
    {
        // ─── Social ─────────────────────────────────────────────
        public int FriendRequests { get; set; }
        public int UnreadDirectMessages { get; set; }

        // ─── Matches (Home tab) ─────────────────────────────────
        public int UnreadMatchMessages { get; set; }
        public int MatchesWithUnreadChat { get; set; }
        public int MatchesToSchedule { get; set; }

        // Match results an opponent proposed that are waiting for THIS user to
        // confirm or dispute (approval-mode tournaments only).
        public int ResultsToConfirm { get; set; }

        // ─── Organizer (things waiting for the user to approve / manage) ──
        // Pending team join requests on teams where the user is captain.
        public int TeamJoinRequests { get; set; }
        // Pending hub join requests in hubs the user owns or admins.
        public int HubJoinRequests { get; set; }
        // Open match "admin help" requests in tournaments the user manages.
        public int AdminHelpRequests { get; set; }
        // Pending tournament registrations awaiting approval in the user's hubs.
        public int PendingRegistrations { get; set; }

        // ─── Convenience tab totals for the client ──────────────
        public int SocialTotal => FriendRequests + UnreadDirectMessages;
        public int MatchesTotal => UnreadMatchMessages + MatchesToSchedule + ResultsToConfirm;
        // Aggregate of everything the user manages — drives a single "manage" badge
        // on the client if it prefers one dot over per-type counters.
        public int OrganizerTotal => TeamJoinRequests + HubJoinRequests + AdminHelpRequests + PendingRegistrations;
        // Hub-manager subset that cascades down the Hubs tab → hub card → tournament. Equals the
        // sum of the per-hub counts in ApprovalsBreakdownDto, so the tab dot matches the cards.
        // (TeamJoinRequests is a captain concern reachable via Tournaments, not Hubs.)
        public int HubManageTotal => HubJoinRequests + AdminHelpRequests + PendingRegistrations;
    }
}
