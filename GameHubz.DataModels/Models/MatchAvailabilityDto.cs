namespace GameHubz.DataModels.Models
{
    public class MatchAvailabilityDto
    {
        public Guid MatchId { get; set; }

        public List<DateTime> MySlots { get; set; } = new();

        public List<DateTime> OpponentSlots { get; set; } = new();

        public DateTime? ConfirmedTime { get; set; }
    }
}