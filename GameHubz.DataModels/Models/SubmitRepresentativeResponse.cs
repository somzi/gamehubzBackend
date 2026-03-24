namespace GameHubz.DataModels.Models
{
    public class SubmitRepresentativeResponse
    {
        public Guid TeamMatchId { get; set; }
        public string Status { get; set; } = "";
        public TeamMemberDto? HomeRepresentative { get; set; }
        public TeamMemberDto? AwayRepresentative { get; set; }
        public Guid? TieBreakMatchId { get; set; }
    }
}
