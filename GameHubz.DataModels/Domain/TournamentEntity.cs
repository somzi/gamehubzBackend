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

        // When the tournament reached a terminal state (Completed / Cancelled / Deleted), UTC.
        // Cleared on a revert back to InProgress, so it always describes the *current* ending.
        // The evidence cleanup sweep keys its retention window off this: ModifiedOn cannot serve,
        // because any later edit to the row would silently push the window forward.
        // Null on every tournament that ended before migration 79 — the sweep's age backstop is
        // what eventually collects those.
        public DateTime? EndedOn { get; set; }

        // Scheduled registration opening (UTC). When set on creation, the tournament is saved as
        // Draft — invisible in the feed and closed to sign-ups — and the background sweep flips it
        // to RegistrationOpen at this moment, firing the same announcement a manual open would.
        // Null (every tournament created before this feature) = registration is open immediately.
        // Kept after the tournament opens as a record of the schedule it was created with — the
        // sweep can't act on it twice because it only ever looks at Draft rows.
        public DateTime? RegistrationOpensAt { get; set; }

        // Set by the deadline-reminder background sweep once the "registration closing soon"
        // push has been sent for this tournament, so the same hub is never reminded twice.
        // Null = reminder not yet sent.
        public DateTime? RegistrationDeadlineReminderSentOn { get; set; }
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

        // Default number of games a single match is played over. 1 (every pre-existing tournament)
        // means one game decides it, exactly as before the series feature existed. Individual
        // matches may override it — see MatchEntity.BestOf.
        public int BestOf { get; set; } = 1;

        // How a multi-game series is settled: by games won, or by the total score across the games.
        // Reuses TeamWinCondition — the same two criteria the team engine has always offered — so
        // solo series and team ties speak one language. Tournament-wide on purpose: letting rounds
        // disagree would make a league's goal difference incoherent.
        public TeamWinCondition SeriesWinCondition { get; set; }

        // Games in the replay series when a knockout series finishes level. Null = replay the same
        // format as the match itself (a drawn Bo3 is settled by another Bo3).
        public int? TiebreakBestOf { get; set; }

        // Best-of for the knockout phase of a two-phase tournament (groups or Swiss, then a
        // bracket). Null = the knockout is played under the same BestOf as the phase before it,
        // which is what every tournament created before this option did. Meaningless — and ignored
        // — for formats that are a single phase, where BestOf already describes every match.
        public int? KnockoutBestOf { get; set; }

        public bool IsTeamTournament { get; set; }

        // The LINEUP size — how many players each team fields, i.e. how many sub-matches a tie has.
        // Unaffected by reserves.
        public int? TeamSize { get; set; }
        public TeamWinCondition TeamWinCondition { get; set; }

        // When true, a roster may carry bench players on top of its TeamSize starters, and the
        // captain can trade a starter for a reserve between games. False = roster is the lineup.
        public bool AllowReserves { get; set; }

        // How many bench slots each team gets while AllowReserves is on. A team may fill 0..N of
        // them — the bench is optional. Null (or AllowReserves off) means no bench at all.
        public int? MaxReserves { get; set; }
        public Guid? WinnerTeamId { get; set; }
        public TournamentTeamEntity? WinnerTeam { get; set; }

        public int? QualifiersPerGroup { get; set; }
        public int? GroupsCount { get; set; }
        public int? RoundDurationMinutes { get; set; }

        // When true, League and GroupStageWithKnockout formats run a double round-robin:
        // every pair plays twice (reverse fixtures generated in rounds N+1..2N).
        public bool DoubleRoundRobin { get; set; }

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

        // GroupStageWithKnockout / Swiss: whether the knockout phase is single- or double-elimination.
        // Null = Single (back-compat for every existing tournament). Double is solo-only — the engine
        // has no team double-elimination, so this is ignored for team tournaments.
        public KnockoutEliminationType? KnockoutEliminationType { get; set; }

        // How the opening fixtures were decided when the bracket was generated (random shuffle,
        // hand-placed by the organiser, standard seeding, or a pot draw). Set once at generation
        // time and never edited afterwards. Null on tournaments generated before the draw picker
        // existed — those were all random.
        public BracketSeedingMode? BracketSeedingMode { get; set; }

        // When true, single-elimination brackets also generate a play-off match
        // between the two semi-final losers. Default false for all existing tournaments.
        public bool HasThirdPlaceMatch { get; set; }

        // When true, a reported match result becomes a pending proposal that the opponent
        // (or an admin / hub owner) must approve before the bracket advances.
        public bool RequireResultApproval { get; set; }

        // When true, a match with an agreed kick-off time runs a ready check: both sides confirm
        // they are at the keyboard, and the side that shows up alone wins by forfeit once the
        // grace period runs out. Only ever applies to matches that actually have a scheduled time
        // — a fixture the pair arranged in chat is reported exactly as it always was.
        public bool RequireMatchCheckIn { get; set; }

        // How long the opponent of a checked-in player has to check in before losing the match by
        // forfeit, in minutes. Counted from the kick-off time, or from the first check-in when that
        // lands later (a player who is late himself cannot burn the other's grace). Null = the
        // system default (see MatchCheckInRules.DefaultGraceMinutes); meaningless while
        // RequireMatchCheckIn is off.
        public int? CheckInGraceMinutes { get; set; }

        // When true, a participant has to verify a match result before reporting it: a biometric
        // unlock on a registered phone plus a screen recording of the final score, tied together in
        // one MatchResultVerification row. Organizers are outside it — they are the escape hatch, as
        // with the ready check. Editable for the whole life of the tournament: it only ever gates
        // reports still to be made.
        public bool RequireResultVerification { get; set; }

        // When true, only hub members with an Exclusive-or-higher role (Exclusive/Admin/Owner)
        // can see this tournament in their feed and register. Default false = open to all members.
        public bool IsExclusive { get; set; }

        public List<TournamentRegistrationEntity>? TournamentRegistrations { get; set; } = new();
        public List<TournamentStageEntity>? TournamentStages { get; set; } = new();
        public List<TournamentParticipantEntity>? TournamentParticipants { get; set; } = new();
    }
}