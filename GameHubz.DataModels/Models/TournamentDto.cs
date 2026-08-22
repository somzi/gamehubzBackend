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

        public bool HasThirdPlaceMatch { get; set; }

        public bool RequireResultApproval { get; set; }

        /// <summary>When true, the tournament is restricted to exclusive-or-higher hub members.</summary>
        public bool IsExclusive { get; set; }

        public List<TournamentRegistrationDto>? TournamentRegistrations { get; set; } = new();

        public List<MatchDto>? Matches { get; set; } = new();
        public List<TournamentStageDto>? TournamentStages { get; set; } = new();

        public List<TournamentParticipantDto>? TournamentParticipants { get; set; } = new();
    }
}