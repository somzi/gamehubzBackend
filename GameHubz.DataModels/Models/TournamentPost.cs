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

        // GroupStageWithKnockout / Swiss: single- vs double-elimination knockout phase.
        // Null = Single (back-compat — old clients omit it). Solo-only; ignored for team tournaments.
        public KnockoutEliminationType? KnockoutEliminationType { get; set; }

        public bool HasThirdPlaceMatch { get; set; }

        public bool RequireResultApproval { get; set; }

        /// <summary>
        /// When true, the tournament is exclusive-only: visible/joinable only to hub members whose
        /// role is Exclusive or higher (Exclusive/Admin/Owner). False/omitted = open to all members.
        /// </summary>
        public bool IsExclusive { get; set; }

        /// <summary>
        /// Games a single match is played over (1 = one game decides it). Applies to solo matches
        /// and, in team tournaments, to each individual sub-match — the team tie itself is still
        /// settled by <see cref="TeamWinCondition"/> over those sub-matches.
        /// Null means "not sent" and preserves the persisted value on an edit — the currently
        /// shipped client already sets <see cref="AllowStructuralEdits"/> but predates these fields,
        /// so that flag cannot tell us whether the format was actually chosen. Null on create
        /// defaults to 1.
        /// </summary>
        public int? BestOf { get; set; }

        /// <summary>
        /// How a multi-game series is settled: <see cref="TeamWinCondition.MatchWins"/> (games won)
        /// or <see cref="TeamWinCondition.AggregateScore"/> (total score across the games).
        /// Tournament-wide — per-round criteria would make goal difference incoherent.
        /// Nullable for the same reason as <see cref="BestOf"/>: absence means "not sent".
        /// </summary>
        public TeamWinCondition? SeriesWinCondition { get; set; }

        /// <summary>
        /// Games in the replay series when a knockout series finishes level. Null = replay the same
        /// format as the match (a drawn Bo3 is settled by another Bo3). Travels with
        /// <see cref="BestOf"/>, so it is preserved whenever that one is absent.
        /// </summary>
        public int? TiebreakBestOf { get; set; }

        public bool IsTeamTournament { get; set; }
        public int? TeamSize { get; set; }
        public TeamWinCondition TeamWinCondition { get; set; }

        /// <summary>
        /// Team tournaments: let rosters carry bench players on top of <see cref="TeamSize"/>.
        /// Structural — only applied when <see cref="AllowStructuralEdits"/> is set on an edit.
        /// </summary>
        public bool AllowReserves { get; set; }

        /// <summary>
        /// Bench slots per team when <see cref="AllowReserves"/> is on. Teams may fill 0..N of them.
        /// Null/0 with the flag on means the organizer opened the option but granted no slots yet.
        /// </summary>
        public int? MaxReserves { get; set; }

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