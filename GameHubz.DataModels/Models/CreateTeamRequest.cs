namespace GameHubz.DataModels.Models
{
    public class CreateTeamRequest
    {
        public Guid TournamentId { get; set; }
        public string TeamName { get; set; } = "";
        public bool RequiresApproval { get; set; }
    }
}
