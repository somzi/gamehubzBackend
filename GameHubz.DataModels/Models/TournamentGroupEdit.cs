namespace GameHubz.DataModels.Models
{
    public class TournamentGroupEdit
    {
        public Guid? Id { get; set; }

        public string Name { get; set; } = "";

        public Guid? TournamentStageId { get; set; }

        public List<TournamentStageEdit>? TournamentStages { get; set; } = new();


    }
}