using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class TournamentStageStructureDto
    {
        public Guid StageId { get; set; }
        public StageType Type { get; set; }
        public int Order { get; set; }
        public string Name { get; set; } = "";

        // Populated if it's a Bracket (Single/Double Elimination)
        public List<BracketRoundDto>? Rounds { get; set; }

        // Populated if it's a Group Stage or League
        public List<GroupDto>? Groups { get; set; }
    }
}