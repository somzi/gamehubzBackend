namespace GameHubz.DataModels.Models
{
    public class TournamentParticipantDto
    {
        public Guid? Id { get; set; }

        public Guid? TournamentId { get; set; }

        public Guid? UserId { get; set; }
    }
}