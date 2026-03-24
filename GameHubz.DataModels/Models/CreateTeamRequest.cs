namespace GameHubz.DataModels.Models
{
    public class CreateTeamRequest
    {
        public Guid TournamentId { get; set; }
        public string TeamName { get; set; } = "";
    }
}
