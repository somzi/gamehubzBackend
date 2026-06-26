namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// Lightweight team summary used to resolve a shared /team/{id} link: just
    /// enough for the recipient's app to redirect into the right tournament and
    /// prompt a join / request-to-join.
    /// </summary>
    public class TeamShareSummaryDto
    {
        public Guid TeamId { get; set; }
        public Guid TournamentId { get; set; }
        public string TeamName { get; set; } = "";
        public bool RequiresApproval { get; set; }
        public int MemberCount { get; set; }
        public int? TeamSize { get; set; }
    }
}
