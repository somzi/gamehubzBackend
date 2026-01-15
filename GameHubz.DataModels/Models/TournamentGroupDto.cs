namespace GameHubz.DataModels.Models
{
    public class TournamentGroupDto
    {
        public Guid? Id { get; set; }
        public string Name { get; set; } = "";

        public Guid? TournamentStageId { get; set; }

        public List<TournamentStageDto>? TournamentStages { get; set; } = new();


    }
}