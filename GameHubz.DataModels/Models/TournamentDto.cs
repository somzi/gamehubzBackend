using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class TournamentDto
    {
        public Guid? Id { get; set; }

        public Guid? HubId { get; set; }

        public string Name { get; set; } = "";

        public string? Description { get; set; }

        public string? Rules { get; set; }

        public TournamentStatus Status { get; set; }

        public int MaxPlayers { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime? RegistrationDeadline { get; set; }

        /// <summary>
        /// Scheduled opening of registration (UTC), or null when registration was open from creation.
        /// While <see cref="Status"/> is <see cref="TournamentStatus.Draft"/> this is a future moment
        /// the client renders as "registration opens …"; afterwards it is just a record of when it did.
        /// </summary>
        public DateTime? RegistrationOpensAt { get; set; }

        public RegionType Region { get; set; }

        /// <summary>ISO 3166-1 alpha-2 country codes when country-scoped, else null (region-scoped).</summary>
        public List<string>? Countries { get; set; }

        public int Prize { get; set; }

        public Guid CreatedBy { get; set; }
        public PrizeCurrency PrizeCurrency { get; set; }

        public TeamWinCondition TeamWinCondition { get; set; }

        /// <summary>Games a single match is played over. 1 = one game decides it.</summary>
        public int BestOf { get; set; } = 1;

        /// <summary>How a multi-game series is settled: games won, or total score across the games.</summary>
        public TeamWinCondition SeriesWinCondition { get; set; }

        /// <summary>Games in the tiebreak replay of a level knockout series. Null = same format as the match.</summary>
        public int? TiebreakBestOf { get; set; }

        /// <summary>
        /// Games a knockout match is played over when a bracket follows a group stage or Swiss.
        /// Null = the knockout is played under the same <see cref="BestOf"/> as the phase before it.
        /// </summary>
        public int? KnockoutBestOf { get; set; }

        public bool HasThirdPlaceMatch { get; set; }

        public bool RequireResultApproval { get; set; }

        /// <summary>Ready check on scheduled matches — see TournamentEntity.RequireMatchCheckIn.</summary>
        public bool RequireMatchCheckIn { get; set; }

        /// <summary>Grace minutes for the ready check. Null = the system default.</summary>
        public int? CheckInGraceMinutes { get; set; }

        /// <summary>Result verification before a report — see TournamentEntity.RequireResultVerification.</summary>
        public bool RequireResultVerification { get; set; }

        /// <summary>When true, the tournament is restricted to exclusive-or-higher hub members.</summary>
        public bool IsExclusive { get; set; }

        public List<TournamentRegistrationDto>? TournamentRegistrations { get; set; } = new();

        public List<MatchDto>? Matches { get; set; } = new();
        public List<TournamentStageDto>? TournamentStages { get; set; } = new();

        public List<TournamentParticipantDto>? TournamentParticipants { get; set; } = new();
    }
}