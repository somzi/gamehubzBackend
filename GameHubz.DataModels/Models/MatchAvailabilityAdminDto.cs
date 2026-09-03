using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// Both sides' scheduling state, for an organizer rather than a player.
    /// <see cref="MatchAvailabilityDto"/> is caller-relative ("my slots" / "the opponent's"), which
    /// is meaningless — and silently misleading — for a manager who is neither participant: the
    /// home/away test in that projection falls through and labels the away side as "mine".
    /// This one names the sides outright.
    /// </summary>
    public class MatchAvailabilityAdminDto
    {
        public Guid MatchId { get; set; }

        /// <summary>Set once the two lists intersected and the match auto-scheduled.</summary>
        public DateTime? ConfirmedTime { get; set; }

        public DateTime? MatchDeadline { get; set; }

        public MatchAvailabilitySideDto Home { get; set; } = new();

        public MatchAvailabilitySideDto Away { get; set; } = new();

        /// <summary>
        /// Slots both sides offered. Empty while one side is still silent — but ALSO empty when both
        /// answered and simply share no hour, which is the case an organizer must not mistake for a
        /// no-show. Computed after materialisation; EF can't intersect two JSON columns in SQL.
        /// </summary>
        public List<DateTime> OverlappingSlots { get; set; } = new();
    }

    public class MatchAvailabilitySideDto
    {
        public Guid? UserId { get; set; }

        /// <summary>Display name — the player's, or the team's on a team fixture.</summary>
        public string? Name { get; set; }

        public string? AvatarUrl { get; set; }

        /// <summary>
        /// When this side submitted. Null on matches whose slots predate migration 78, so it says
        /// "unknown", never "never" — <see cref="Slots"/> is what answers whether they submitted.
        /// </summary>
        public DateTime? SubmittedOn { get; set; }

        [JsonIgnore]
        public string? SlotsJson { get; set; }

        public List<DateTime> Slots
        {
            get => string.IsNullOrEmpty(SlotsJson)
                ? new List<DateTime>()
                : JsonSerializer.Deserialize<List<DateTime>>(SlotsJson)!
                    .Select(d => DateTime.SpecifyKind(d, DateTimeKind.Utc)).ToList();
            set => SlotsJson = JsonSerializer.Serialize(value);
        }

        public bool HasSubmitted => Slots.Count > 0;
    }
}
