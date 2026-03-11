namespace GameHubz.DataModels.Models
{
    public class BracketRoundDto
    {
        public int RoundNumber { get; set; }
        public string Name { get; set; } = "";
        public DateTime? RoundDeadline { get; set; }
        public List<MatchStructureDto> Matches { get; set; } = new();
    }
}