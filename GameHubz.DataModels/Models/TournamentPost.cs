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

        // League / Group-Stage: every pair plays twice instead of once.
        public bool DoubleRoundRobin { get; set; }

        // Swiss format: number of rounds. Null = auto (ceil(log2(participants))).
        public int? SwissRoundsCount { get; set; }

        // Swiss format: knockout bracket size after the rounds (power of 2). Null = pure Swiss.
        public int? SwissKnockoutQualifiers { get; set; }

        // Swiss format: direct knockout berths; the rest of the bracket is filled via a
        // play-in round between standings D+1 .. D+2(N-D). Null/== N = no play-in.
        public int? SwissDirectQualifiers { get; set; }

        public bool HasThirdPlaceMatch { get; set; }

        public bool RequireResultApproval { get; set; }

        /// <summary>
        /// When true, the tournament is exclusive-only: visible/joinable only to hub members whose
        /// role is Exclusive or higher (Exclusive/Admin/Owner). False/omitted = open to all members.
        /// </summary>
        public bool IsExclusive { get; set; }

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

        /// <summary>
        /// Opt-in marker for the new edit modal: when true and the tournament has not started, the
        /// server applies TeamSize / TeamWinCondition / IsExclusive / Countries / DoubleRoundRobin
        /// from the payload instead of preserving them. Old clients (that don't include these fields
        /// in the edit body) leave it false, so their saves keep preserving the persisted values.
        /// Ignored on create. <see cref="IsTeamTournament"/> stays locked either way.
        /// </summary>
        public bool AllowStructuralEdits { get; set; }
    }
}