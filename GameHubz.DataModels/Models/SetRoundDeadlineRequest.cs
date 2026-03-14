namespace GameHubz.DataModels.Models
{
    public class SetRoundDeadlineRequest
    {
        public int RoundNumber { get; set; }
        public DateTime? Deadline { get; set; }
        public DateTime? RoundStart { get; set; }
    }
}