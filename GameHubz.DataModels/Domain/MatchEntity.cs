using GameHubz.Common;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace GameHubz.DataModels.Domain
{
    public class MatchEntity : BaseEntity
    {
        public Guid TournamentId { get; set; }
        public int? RoundNumber { get; set; }
        public Guid? HomeParticipantId { get; set; }
        public Guid? AwayParticipantId { get; set; }
        public int? HomeUserScore { get; set; }
        public int? AwayUserScore { get; set; }
        public DateTime? ScheduledStartTime { get; set; }
        public DateTime? RoundDeadline { get; set; }

        // How far the round-deadline reminder waves have progressed for this match, set by the
        // background sweep: 0 = none, 1 = early (24h) reminder sent, 2 = last-call sent (done).
        public int RoundReminderStage { get; set; }

        public MatchStatus Status { get; set; }
        public Guid? WinnerParticipantId { get; set; }
        public TournamentEntity? Tournament { get; set; }
        public TournamentParticipantEntity? HomeParticipant { get; set; }
        public TournamentParticipantEntity? AwayParticipant { get; set; }
        public TournamentParticipantEntity? WinnerParticipant { get; set; }
        public Guid? TournamentStageId { get; set; }
        public TournamentStageEntity? TournamentStage { get; set; }
        public MatchStage Stage { get; set; }
        public Guid? TournamentGroupId { get; set; }
        public TournamentGroupEntity? TournamentGroup { get; set; }
        public int? MatchOrder { get; set; }
        public Guid? NextMatchId { get; set; }
        public MatchEntity? NextMatch { get; set; }
        public Guid? NextMatchLoserBracketId { get; set; }
        public MatchEntity? NextMatchLoserBracket { get; set; }
        public bool IsUpperBracket { get; set; } = true;

        public string? HomeSlotsJson { get; set; }
        public string? AwaySlotsJson { get; set; }

        [NotMapped]
        public List<DateTime> HomeSlots
        {
            get => string.IsNullOrEmpty(HomeSlotsJson)
                ? new List<DateTime>()
                : JsonSerializer.Deserialize<List<DateTime>>(HomeSlotsJson)!
                    .Select(d => DateTime.SpecifyKind(d, DateTimeKind.Utc)).ToList();
            set => HomeSlotsJson = JsonSerializer.Serialize(value);
        }

        [NotMapped]
        public List<DateTime> AwaySlots
        {
            get => string.IsNullOrEmpty(AwaySlotsJson)
                ? new List<DateTime>()
                : JsonSerializer.Deserialize<List<DateTime>>(AwaySlotsJson)!
                    .Select(d => DateTime.SpecifyKind(d, DateTimeKind.Utc)).ToList();
            set => AwaySlotsJson = JsonSerializer.Serialize(value);
        }

        // Per-match series format. Null falls back to the tournament default, so the organizer can
        // override a whole round (SetRoundBestOf stamps every unplayed match in it) or a single
        // fixture. Locked the moment the match has any recorded game — see GamesJson.
        public int? BestOf { get; set; }
        public int? TiebreakBestOf { get; set; }

        // The played games, as a JSON list of SeriesGame ({HomeScore, AwayScore, SeriesNumber}).
        // Null / empty means nothing has been reported, which is also what makes it the format lock:
        // once a game exists, BestOf for this match can no longer change.
        public string? GamesJson { get; set; }

        // The same list carried by a pending proposal in result-approval tournaments, so approving
        // replays the exact series the participant reported rather than just its headline score.
        public string? ProposedGamesJson { get; set; }

        // Real goals across every game of every series, denormalised at write time. HomeUserScore
        // holds the *headline* (games won under MatchWins), which must not drive goal difference —
        // standings read these instead. Null on matches reported before the series feature, where
        // the headline is the score, so readers fall back to HomeUserScore/AwayUserScore.
        public int? HomeGoalsTotal { get; set; }
        public int? AwayGoalsTotal { get; set; }

        [NotMapped]
        public List<SeriesGame> Games
        {
            get => string.IsNullOrEmpty(GamesJson)
                ? new List<SeriesGame>()
                : JsonSerializer.Deserialize<List<SeriesGame>>(GamesJson)!;
            set => GamesJson = value == null || value.Count == 0 ? null : JsonSerializer.Serialize(value);
        }

        [NotMapped]
        public List<SeriesGame> ProposedGames
        {
            get => string.IsNullOrEmpty(ProposedGamesJson)
                ? new List<SeriesGame>()
                : JsonSerializer.Deserialize<List<SeriesGame>>(ProposedGamesJson)!;
            set => ProposedGamesJson = value == null || value.Count == 0 ? null : JsonSerializer.Serialize(value);
        }

        public List<MatchEvidenceEntity>? MatchEvidences { get; set; } = new();

        public List<MatchChatEntity>? MatchChats { get; set; } = new();
        public DateTime? RoundOpenAt { get; set; }
        public Guid? TeamMatchId { get; set; }
        public TeamMatchEntity? TeamMatch { get; set; }

        public Guid? HomeUserId { get; set; }
        public UserEntity? HomeUser { get; set; }
        public Guid? AwayUserId { get; set; }
        public UserEntity? AwayUser { get; set; }

        // Pending proposal state for tournaments with result approval enabled.
        // Non-null ProposedByUserId marks an active proposal awaiting opponent / admin approval.
        public int? ProposedHomeScore { get; set; }
        public int? ProposedAwayScore { get; set; }
        public Guid? ProposedByUserId { get; set; }

        // Admin-help escalation: a participant can flag the match so hub admins / the hub owner
        // get notified and can step into the match chat. Resolving clears all three fields.
        public bool AdminHelpRequested { get; set; }
        public Guid? AdminHelpRequestedByUserId { get; set; }
        public DateTime? AdminHelpRequestedOn { get; set; }

        // Explicit destination slot overrides. Default (null) preserves the legacy MatchOrder%2
        // pairing used by single-elimination. Double-elimination sets these because LB drop-in
        // edges don't follow the bracket-pair convention (e.g. a WB loser entering an LB match
        // must always take the away slot, regardless of its source MatchOrder).
        // 0 = home slot, 1 = away slot.
        public int? NextMatchHomeAwaySlot { get; set; }
        public int? NextMatchLoserBracketHomeAwaySlot { get; set; }
    }
}