namespace GameHubz.DataModels.Models
{
    public class TournamentParticipantOverview
    {
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public Guid UserId { get; set; }
        public bool IsTeamTournament { get; set; }
        public Guid? TeamId { get; set; }
        public string? TeamName { get; set; }
        public Guid? CaptainUserId { get; set; }
        public int MemberCount { get; set; }
        public int? TeamSize { get; set; }
        public List<TournamentParticipantMemberOverview> Members { get; set; } = new();
    }

    public class TournamentParticipantMemberOverview
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
    }
}