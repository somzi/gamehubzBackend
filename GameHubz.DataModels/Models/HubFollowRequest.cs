namespace GameHubz.DataModels.Models
{
    public class HubFollowRequest
    {
        public Guid HubId { get; set; }
        public Guid? UserId { get; set; }
    }
}