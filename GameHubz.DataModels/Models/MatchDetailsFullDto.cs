using System.Text.Json.Serialization;

namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// Envelope returned by GET /api/match/{id}/details/full. Bundles the three pieces mobile
    /// used to fetch back-to-back when opening a match modal (details + streams + optional
    /// availability) into a single response, so the modal opens in one round-trip. The
    /// <see cref="Details"/> field is untyped so it can carry either MatchResultDetailDto
    /// (solo) or TeamMatchDetailsDto (team sub-match) — same polymorphism the legacy
    /// /details endpoint already had.
    /// </summary>
    public class MatchDetailsFullDto
    {
        public object Details { get; set; } = null!;

        public List<MatchStreamDto> Streams { get; set; } = new();

        // Populated only when the caller has an availability record for this match (pending
        // matches or if they've already submitted). Omitted from the JSON when null so team
        // sub-matches and scheduled/completed matches don't ship an empty object.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MatchAvailabilityDto? Availability { get; set; }
    }
}
