namespace GameHubz.DataModels.Models
{
    public class TournamentRegistrationOverview
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
    }
}