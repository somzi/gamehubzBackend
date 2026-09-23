using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class TeamMatchDetailsDto
    {
        public Guid TeamMatchId { get; set; }
        public TeamMatchStatus Status { get; set; }
        public Guid? WinnerTeamParticipantId { get; set; }
        public Guid? HomeTeamParticipantId { get; set; }
        public Guid? AwayTeamParticipantId { get; set; }
        public TeamWinCondition WinCondition { get; set; }

        /// <summary>
        /// How each individual sub-match series is settled — separate from <see cref="WinCondition"/>,
        /// which settles the tie itself over those sub-matches.
        /// </summary>
        public TeamWinCondition SeriesWinCondition { get; set; }

        public TeamMatchTeamInfoDto? HomeTeam { get; set; }
        public TeamMatchTeamInfoDto? AwayTeam { get; set; }
        public List<TeamSubMatchDto> SubMatches { get; set; } = new();
        public TeamAggregateScoreDto? AggregateScore { get; set; }
        public TeamTieBreakInfoDto? TieBreak { get; set; }
        public bool RequireResultApproval { get; set; }

        /// <summary>
        /// Ready check for this tournament, with its grace window. Each GAME of the tie runs its
        /// own check (see TeamSubMatchDto) — the tie itself has no kick-off to turn up for.
        /// </summary>
        public bool RequireMatchCheckIn { get; set; }
        public int? CheckInGraceMinutes { get; set; }

        /// <summary>
        /// Result verification for this tournament. Like the ready check it belongs to each GAME of the
        /// tie: the player nominated for a game verifies that game's result.
        /// </summary>
        public bool RequireResultVerification { get; set; }
    }

    public class TeamMatchTeamInfoDto
    {
        public Guid TeamId { get; set; }
        public string TeamName { get; set; } = "";
        public Guid? CaptainUserId { get; set; }
        public List<TeamMemberDto> Members { get; set; } = new();
    }

    public class TeamSubMatchDto
    {
        public Guid MatchId { get; set; }
        public TeamMemberDto? HomePlayer { get; set; }
        public TeamMemberDto? AwayPlayer { get; set; }
        public int? HomeScore { get; set; }
        public int? AwayScore { get; set; }
        public MatchStatus Status { get; set; }
        public Guid? WinnerUserId { get; set; }
        public bool IsTieBreakMatch { get; set; }
        public List<string> Evidences { get; set; } = [];

        /// <summary>Same evidence, typed. See MatchEvidenceItemDto for why both exist.</summary>
        public List<MatchEvidenceItemDto> EvidenceItems { get; set; } = [];
        public int? ProposedHomeScore { get; set; }
        public int? ProposedAwayScore { get; set; }
        public Guid? ProposedByUserId { get; set; }
        public bool AdminHelpRequested { get; set; }
        public Guid? AdminHelpRequestedByUserId { get; set; }

        /// <summary>Series format for this individual game, resolved against the tournament default.</summary>
        public int BestOf { get; set; } = 1;

        public int? TiebreakBestOf { get; set; }

        /// <summary>Games played in this individual match, main series first, then tiebreaks.</summary>
        public List<SeriesGame>? Games { get; set; }

        public List<SeriesGame>? ProposedGames { get; set; }

        /// <summary>Agreed kick-off for this individual game, when the pair have settled on one.</summary>
        public DateTime? ScheduledStartTime { get; set; }

        /// <summary>
        /// Ready-check state of this game. The two computed moments are null unless the game is
        /// actually running a check (tournament setting on, kick-off agreed, nothing decided yet).
        /// </summary>
        public DateTime? HomeCheckedInOn { get; set; }
        public DateTime? AwayCheckedInOn { get; set; }
        public DateTime? CheckInOpensAt { get; set; }
        public DateTime? CheckInDeadline { get; set; }

        /// <summary>
        /// This game carries verification records. Per game, because each game of the tie is verified
        /// by its own player; see MatchResultDetailDto.HasResultVerifications for why it exists.
        /// </summary>
        public bool HasResultVerifications { get; set; }
    }

    public class TeamAggregateScoreDto
    {
        public int HomeTeamWins { get; set; }
        public int AwayTeamWins { get; set; }
        public int HomeTeamTotalScore { get; set; }
        public int AwayTeamTotalScore { get; set; }
    }

    public class TeamTieBreakInfoDto
    {
        public bool IsRequired { get; set; }
        public TeamMemberDto? HomeRepresentative { get; set; }
        public TeamMemberDto? AwayRepresentative { get; set; }
    }
}
