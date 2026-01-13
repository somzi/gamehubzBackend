namespace GameHubz.DataModels.Models
{
    public class TournamentRegistrationDto
    {
        public Guid? Id { get; set; }
        public Guid? TournamentId { get; set; }

        public Guid? UserId { get; set; }

        public string? Status { get; set; }

        public TournamentDto? Tournament { get; set; }

        public UserDto? User { get; set; }
    }
}