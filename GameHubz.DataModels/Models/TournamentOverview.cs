using System.Linq;
using System.Text.Json.Serialization;
using GameHubz.DataModels.Catalog;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class TournamentOverview
    {
        public string Name { get; set; } = string.Empty;
        public Guid Id { get; set; }
        public Guid HubId { get; set; }
        public RegionType Region { get; set; }

        /// <summary>
        /// ISO 3166-1 alpha-2 country codes when the tournament is country-scoped, else null
        /// (region-scoped). Omitted from JSON when null so region tournaments look unchanged.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Countries { get; set; }

        /// <summary>Display names for <see cref="Countries"/> (from the catalog). Omitted when none.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? CountryNames =>
            Countries?.Select(c => CountryCatalog.Get(c)?.Name).Where(n => n != null).Select(n => n!).ToList();

        /// <summary>Flag emojis for <see cref="Countries"/> (from the catalog). Omitted when none.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? CountryFlags =>
            Countries?.Select(c => CountryCatalog.Get(c)?.Flag).Where(f => f != null).Select(f => f!).ToList();
        public DateTime StartDate { get; set; }
        public int NumberOfParticipants { get; set; }
        public int MaxPlayers { get; set; }
        public int Prize { get; set; }
        public PrizeCurrency PrizeCurrency { get; set; }
        public TournamentStatus Status { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Rules { get; set; } = string.Empty;
        public Guid CreatedBy { get; set; }
        public DateTime? RegistrationDeadLine { get; set; }
        public string HubName { get; set; } = string.Empty;
        public string? HubAvatarUrl { get; set; }
        public TournamentFormat? Format { get; set; }
        public int? RoundDurationMinutes { get; set; }
        public bool? IsTeamTournament { get; set; }
        public int? TeamSize { get; set; }
        public TeamWinCondition? TeamWinCondition { get; set; }
        public bool HasThirdPlaceMatch { get; set; }
        public bool RequireResultApproval { get; set; }

        /// <summary>
        /// True when the requesting user may perform owner-level actions (hub owner, hub admin or
        /// platform admin). Only populated by the v2 overview endpoint; omitted from the v1 payload
        /// so the legacy client keeps receiving an unchanged response.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool CanManage { get; set; }
    }
}