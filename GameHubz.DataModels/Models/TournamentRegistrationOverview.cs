namespace GameHubz.DataModels.Models
{
    public class TournamentRegistrationOverview
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public bool IsTeamRegistration { get; set; }
        public Guid? TeamId { get; set; }
        public string? TeamName { get; set; }
        public Guid? CaptainUserId { get; set; }
        public int? MemberCount { get; set; }
    }
}