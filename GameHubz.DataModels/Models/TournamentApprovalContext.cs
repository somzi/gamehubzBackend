namespace GameHubz.DataModels.Models
{
    public class TournamentApprovalContext
    {
        public Guid HubOwnerUserId { get; set; }
        public bool RequireResultApproval { get; set; }
    }
}
