namespace GameHubz.DataModels.Models
{
    public class TournamentParticipantOverview
    {
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public Guid UserId { get; set; }
    }
}