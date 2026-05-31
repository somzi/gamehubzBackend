using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class FriendRequestDto
    {
        public Guid Id { get; set; }
        public Guid FromUserId { get; set; }
        public string FromUsername { get; set; } = "";
        public string? FromNickname { get; set; }
        public string? FromAvatarUrl { get; set; }
        public Guid ToUserId { get; set; }
        public string ToUsername { get; set; } = "";
        public string? ToNickname { get; set; }
        public string? ToAvatarUrl { get; set; }
        public FriendRequestStatus Status { get; set; }
        public DateTime CreatedOn { get; set; }
    }
}
