using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    public class TournamentEntity : BaseEntity
    {
        public Guid? HubId { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? Rules { get; set; }
        public TournamentStatus Status { get; set; }
        public int? MaxPlayers { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? RegistrationDeadline { get; set; }
        public TournamentFormat Format { get; set; }
        public HubEntity? Hub { get; set; }
        public int Prize { get; set; }
        public PrizeCurrency PrizeCurrency { get; set; }
        public RegionType Region { get; set; }

        /// <summary>
        /// ISO 3166-1 alpha-2 country codes when the tournament is country-scoped, or null when it
        /// is region-scoped (uses <see cref="Region"/>). When set, the tournament is visible only to
        /// users whose country is in this list. Stored as a Postgres text[] array. Null (never empty)
        /// means region-scoped. <see cref="Region"/> is derived from the first country for display.
        /// </summary>
        public List<string>? Countries { get; set; }
        public Guid? WinnerUserId { get; set; }
        public UserEntity? WinnerUser { get; set; }

        public bool IsTeamTournament { get; set; }
        public int? TeamSize { get; set; }
        public TeamWinCondition TeamWinCondition { get; set; }
        public Guid? WinnerTeamId { get; set; }
        public TournamentTeamEntity? WinnerTeam { get; set; }

        public int? QualifiersPerGroup { get; set; }
        public int? GroupsCount { get; set; }
        public int? RoundDurationMinutes { get; set; }

        // Swiss format: number of rounds chosen by the organizer. Null = auto
        // (ceil(log2(participants)), the standard Swiss round count).
        public int? SwissRoundsCount { get; set; }

        // Swiss format: knockout bracket size after the Swiss rounds (power of 2).
        // Null = pure Swiss — the standings leader wins the tournament outright.
        public int? SwissKnockoutQualifiers { get; set; }

        // Swiss format: how many of the knockout slots are direct berths from the standings.
        // The remaining (N - D) slots are decided by a play-in round between standings
        // D+1 .. D+2(N-D). Null or == SwissKnockoutQualifiers = no play-in.
        public int? SwissDirectQualifiers { get; set; }

        // When true, single-elimination brackets also generate a play-off match
        // between the two semi-final losers. Default false for all existing tournaments.
        public bool HasThirdPlaceMatch { get; set; }

        // When true, a reported match result becomes a pending proposal that the opponent
        // (or an admin / hub owner) must approve before the bracket advances.
        public bool RequireResultApproval { get; set; }

        public List<TournamentRegistrationEntity>? TournamentRegistrations { get; set; } = new();
        public List<TournamentStageEntity>? TournamentStages { get; set; } = new();
        public List<TournamentParticipantEntity>? TournamentParticipants { get; set; } = new();
    }
}