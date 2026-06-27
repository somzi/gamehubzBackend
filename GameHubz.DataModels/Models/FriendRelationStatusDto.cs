namespace GameHubz.DataModels.Models
{
    public enum FriendRelationStatus
    {
        None = 0,
        Friends = 1,
        OutgoingRequest = 2,
        IncomingRequest = 3,
        BlockedByMe = 4,
        BlockedByOther = 5,
        Self = 6
    }

    public class FriendRelationStatusDto
    {
        public Guid OtherUserId { get; set; }
        public FriendRelationStatus Status { get; set; }
        public Guid? RequestId { get; set; }
    }
}
