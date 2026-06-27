namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// Lightweight projection used by authorization checks to resolve the hub
    /// a tournament belongs to and that hub's owner without loading the full graph.
    /// </summary>
    public class HubOwnershipInfo
    {
        public Guid HubId { get; set; }

        public Guid OwnerUserId { get; set; }
    }
}
