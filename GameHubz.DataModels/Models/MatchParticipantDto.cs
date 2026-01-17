namespace GameHubz.DataModels.Models
{
    public class MatchParticipantDto
    {
        public Guid ParticipantId { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; } = "TBD";
        public int? Score { get; set; }
        public bool IsWinner { get; set; }
        public int? Seed { get; set; }
    }
}