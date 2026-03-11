namespace GameHubz.DataModels.Models
{
    public class GroupDto
    {
        public Guid GroupId { get; set; }
        public string Name { get; set; } = "";
        public List<LeagueStandingDto> Standings { get; set; } = new();
        public List<MatchStructureDto> Matches { get; set; } = new();
        public Dictionary<int, DateTime?> RoundDeadlines { get; set; } = new();
    }
}