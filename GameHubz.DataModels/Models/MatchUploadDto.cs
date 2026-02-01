namespace GameHubz.DataModels.Models
{
    public class MatchUploadDto
    {
        public Guid Id { get; set; }
        public string HubName { get; set; } = string.Empty;
        public string TournamentName { get; set; } = string.Empty;
    }
}