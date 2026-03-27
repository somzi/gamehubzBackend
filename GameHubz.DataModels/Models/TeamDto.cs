namespace GameHubz.DataModels.Models
{
    public class TeamDto
    {
        public Guid TeamId { get; set; }
        public string TeamName { get; set; } = "";
        public Guid CaptainUserId { get; set; }
        public int MemberCount { get; set; }
        public int? TeamSize { get; set; }
        public List<TeamMemberDto> Members { get; set; } = new();
        public bool IsAlreadyRegistred { get; set; }
        public bool IsRegistrationAccepted { get; set; }
    }

    public class TeamMemberDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = "";
        public string? AvatarUrl { get; set; }
    }
}