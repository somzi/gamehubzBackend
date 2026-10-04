namespace GameHubz.DataModels.Models
{
    public class NotificationSourceDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string? AvatarUrl { get; set; }
        public Guid? HubId { get; set; }
        public string? HubName { get; set; }
    }

    public class NotificationSourcePageDto
    {
        public List<NotificationSourceDto> Items { get; set; } = new();
        public int? NextPage { get; set; }
    }

    public readonly record struct NotificationScope(Guid? HubId = null, Guid? TournamentId = null, Guid? MatchId = null, Guid? TeamMatchId = null)
    {
        public bool IsEmpty => HubId == null && TournamentId == null && MatchId == null && TeamMatchId == null;
    }

    public class MutedNotificationRecipient
    {
        public Guid UserId { get; set; }
        public string? PushToken { get; set; }
        public string? DiscordUserId { get; set; }
    }
}
