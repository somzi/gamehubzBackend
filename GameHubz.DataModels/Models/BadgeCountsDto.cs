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

        // ─── Convenience tab totals for the client ──────────────
        public int SocialTotal => FriendRequests + UnreadDirectMessages;
        public int MatchesTotal => UnreadMatchMessages + MatchesToSchedule;
    }
}
