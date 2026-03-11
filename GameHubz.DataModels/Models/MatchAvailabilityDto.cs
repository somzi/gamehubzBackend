using System.Text.Json;

namespace GameHubz.DataModels.Models
{
    public class MatchAvailabilityDto
    {
        public Guid MatchId { get; set; }

        public DateTime? ConfirmedTime { get; set; }

        public string? MySlotsJson { get; set; }

        public string? OpponentSlotsJson { get; set; }

        public DateTime? MatchDeadline { get; set; }

        public List<DateTime> MySlots
        {
            get => string.IsNullOrEmpty(MySlotsJson) ? new List<DateTime>() : JsonSerializer.Deserialize<List<DateTime>>(MySlotsJson)!;
            set => MySlotsJson = JsonSerializer.Serialize(value);
        }

        public List<DateTime> OpponentSlots
        {
            get => string.IsNullOrEmpty(OpponentSlotsJson) ? new List<DateTime>() : JsonSerializer.Deserialize<List<DateTime>>(OpponentSlotsJson)!;
            set => OpponentSlotsJson = JsonSerializer.Serialize(value);
        }
    }
}