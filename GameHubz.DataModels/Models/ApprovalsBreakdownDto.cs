namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// Per-hub and per-tournament breakdown of the items a hub manager (owner / admin) has to
    /// approve. Lets the client cascade the aggregate organizer badge down to the specific hub
    /// card, tournament, and the Requests / Registrations tab. Returned by
    /// GET api/v2/badges/approvals and refreshed alongside the live BadgeCounts push.
    /// </summary>
    public class ApprovalsBreakdownDto
    {
        public List<HubApprovalCount> Hubs { get; set; } = new();
        public List<TournamentApprovalCount> Tournaments { get; set; } = new();
    }

    /// <summary>Total pending approvals in a hub: hub join requests + every tournament's items.
    /// JoinRequests is broken out so the client can badge the hub's Members tab with the hub-level
    /// portion and the Tournaments tab with the rest (Count - JoinRequests).</summary>
    public class HubApprovalCount
    {
        public Guid HubId { get; set; }
        public int Count { get; set; }
        public int JoinRequests { get; set; }
    }

    /// <summary>Pending approvals in a single tournament, split by kind so the client can badge
    /// the Requests/Registrations tab with the registration count specifically.</summary>
    public class TournamentApprovalCount
    {
        public Guid TournamentId { get; set; }
        public Guid HubId { get; set; }
        // Raw TournamentStatus int so the client can bucket into Live (3) / Past (4) / Upcoming
        // (everything else) and badge the matching filter tab without loading every tournament.
        public int Status { get; set; }
        public int Registrations { get; set; }
        public int AdminHelp { get; set; }
        public int Total => Registrations + AdminHelp;
    }

    /// <summary>Internal grouped-count projection (tournament-scoped) used to assemble the breakdown.</summary>
    public class TournamentCountRow
    {
        public Guid TournamentId { get; set; }
        public Guid HubId { get; set; }
        public int Status { get; set; }
        public int Count { get; set; }
    }

    /// <summary>Internal grouped-count projection (hub-scoped) used to assemble the breakdown.</summary>
    public class HubCountRow
    {
        public Guid HubId { get; set; }
        public int Count { get; set; }
    }
}
