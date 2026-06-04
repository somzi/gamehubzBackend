using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Interfaces;

namespace GameHubz.DataModels.Models
{
    public class TournamentPost : IEditableDto
    {
        public Guid? Id { get; set; }

        public Guid? HubId { get; set; }

        public string Name { get; set; } = "";

        public string? Description { get; set; }

        public string? Rules { get; set; }

        public TournamentStatus Status { get; set; }

        public int MaxPlayers { get; set; }

        public DateTime? StartDate { get; set; }

        public TournamentFormat? Format { get; set; }

        public int? QualifiersPerGroup { get; set; }
        public int? GroupsCount { get; set; }
        public int? RoundDurationMinutes { get; set; }

        public bool HasThirdPlaceMatch { get; set; }

        public bool RequireResultApproval { get; set; }

        public bool IsTeamTournament { get; set; }
        public int? TeamSize { get; set; }
        public TeamWinCondition TeamWinCondition { get; set; }

        public DateTime? RegistrationDeadline { get; set; }
        public int Prize { get; set; }
        public PrizeCurrency PrizeCurrency { get; set; }
        public RegionType Region { get; set; }

        /// <summary>
        /// Optional ISO 3166-1 alpha-2 country codes. When non-empty, the tournament is country-scoped
        /// (visible only to users from one of these countries) and Region is derived from the first.
        /// Null/empty = region-scoped.
        /// </summary>
        public List<string>? Countries { get; set; }
    }
}