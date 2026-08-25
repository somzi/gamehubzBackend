namespace GameHubz.DataModels.Models
{
    public class TransferHubOwnershipRequest
    {
        /// <summary>The member who becomes the new hub owner. Must already be a member of the hub.</summary>
        public Guid UserId { get; set; }
    }
}
