using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class TournamentStructureDto
    {
        public Guid TournamentId { get; set; }
        public string Name { get; set; } = "";
        public TournamentFormat Format { get; set; }
        public TournamentStatus Status { get; set; }
        public List<TournamentStageStructureDto> Stages { get; set; } = new();
    }
}