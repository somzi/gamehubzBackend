namespace GameHubz.DataModels.Models
{
    public class SubmitAvailabilityRequest
    {
        public Guid MatchId { get; set; }
        public List<DateTime> SelectedSlots { get; set; } = new();
    }
}