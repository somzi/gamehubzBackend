namespace GameHubz.DataModels.Models
{
    public class TieBreakStatusDto
    {
        public Guid TeamMatchId { get; set; }
        public string Status { get; set; } = "";
        public TeamMemberDto? HomeRepresentative { get; set; }
        public TeamMemberDto? AwayRepresentative { get; set; }
    }
}
