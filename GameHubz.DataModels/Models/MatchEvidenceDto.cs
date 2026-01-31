namespace GameHubz.DataModels.Models
{
    public class MatchEvidenceDto
    {
        public string? Url { get; set; }
        public Guid? Id { get; set; }
        public Guid? MatchId { get; set; }
        public MatchDto? Match { get; set; }
    }
}