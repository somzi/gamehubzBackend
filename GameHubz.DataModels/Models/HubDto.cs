namespace GameHubz.DataModels.Models
{
    public class HubDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid UserId { get; set; }
        public string UserDisplayName { get; set; } = string.Empty;

        public int NumberOfUsers { get; set; }


        public List<TournamentDto>? Tournaments { get; set; } = new();
        public int NumberOfTournaments { get; set; }
    }
}