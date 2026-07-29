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
        public int? SwissRoundsCount { get; set; }
        public int? SwissKnockoutQualifiers { get; set; }
        public int? SwissDirectQualifiers { get; set; }
        public KnockoutEliminationType? KnockoutEliminationType { get; set; }
        public bool? IsTeamTournament { get; set; }
        public int? TeamSize { get; set; }
        public TeamWinCondition? TeamWinCondition { get; set; }

        /// <summary>
        /// Team tournaments: whether a roster may carry bench players beyond <see cref="TeamSize"/>,
        /// with the captain free to trade a starter for a reserve between games. False on every
        /// tournament created before reserves shipped. Omitted from JSON when false (and MaxReserves
        /// when null) so the payload of every existing tournament stays exactly as it was — clients
        /// treat an absent field as "no bench", same as false.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool AllowReserves { get; set; }

        /// <summary>
        /// Bench slots each team gets while <see cref="AllowReserves"/> is on; a team may fill 0..N.
        /// Null means no bench.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MaxReserves { get; set; }
        public bool HasThirdPlaceMatch { get; set; }
        public bool RequireResultApproval { get; set; }
        public bool DoubleRoundRobin { get; set; }
        public int? GroupsCount { get; set; }
        public int? QualifiersPerGroup { get; set; }

        /// <summary>
        /// How this tournament's bracket was drawn (random / manual / seeded / pots). Null on
        /// tournaments generated before the draw picker shipped — all of those were random — and on
        /// tournaments whose bracket hasn't been generated yet.
        /// </summary>
        public BracketSeedingMode? BracketSeedingMode { get; set; }

        /// <summary>
        /// When true, the tournament is restricted to exclusive-or-higher hub members
        /// (Exclusive/Admin/Owner). False = open to all members.
        /// </summary>
        public bool IsExclusive { get; set; }

        /// <summary>
        /// True when the requesting user may perform owner-level actions (hub owner, hub admin or
        /// platform admin). Only populated by the v2 overview endpoint; omitted from the v1 payload
        /// so the legacy client keeps receiving an unchanged response.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool CanManage { get; set; }

        /// <summary>
        /// True when the requesting user passes this tournament's exclusivity gate — i.e. has an
        /// Exclusive-or-higher role in the owning hub (or is a manager). Only populated by the v2
        /// overview endpoint and only meaningful when <see cref="IsExclusive"/> is true; lets the
        /// client hide the Join button for plain members instead of letting registration fail.
        /// Omitted when false so the v1 payload (and non-exclusive feed items) stay unchanged.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool HasExclusiveAccess { get; set; }

        /// <summary>
        /// True when the requesting user is already registered in this tournament (approved or
        /// pending). Only populated by the v3 overview endpoint; lets the mobile client skip the
        /// separate CHECK_REGISTRATION call it used to fire on open (that call is now redundant
        /// whenever the client hits v3). Omitted when false so the v1 / v2 payloads stay unchanged.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool HasUserRegistered { get; set; }
    }
}