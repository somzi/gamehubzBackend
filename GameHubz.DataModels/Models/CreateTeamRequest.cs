namespace GameHubz.DataModels.Models
{
    public class CreateTeamRequest
    {
        public Guid TournamentId { get; set; }
        public string TeamName { get; set; } = "";
        public bool RequiresApproval { get; set; }

        /// <summary>
        /// Join code of a private tournament (see TournamentEntity.IsPrivate). Creating a team is how
        /// a captain enters one, so it is checked here; ignored for public tournaments.
        /// </summary>
        public string? JoinCode { get; set; }
    }
}
