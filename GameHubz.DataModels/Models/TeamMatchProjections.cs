using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class TeamMatchDetailsProjection
    {
        public Guid TeamMatchId { get; set; }
        public TeamMatchStatus Status { get; set; }
        public Guid? WinnerTeamParticipantId { get; set; }
        public Guid? HomeTeamParticipantId { get; set; }
        public Guid? AwayTeamParticipantId { get; set; }
        public Guid? HomeTeamRepresentativeUserId { get; set; }
        public Guid? AwayTeamRepresentativeUserId { get; set; }
        public int? MatchOrder { get; set; }
        public bool RequireResultApproval { get; set; }

        /// <summary>Ready check, read off the tournament — see TournamentEntity.RequireMatchCheckIn.</summary>
        public bool RequireMatchCheckIn { get; set; }
        public int? CheckInGraceMinutes { get; set; }

        /// <summary>Result verification, read off the tournament — see TournamentEntity.RequireResultVerification.</summary>
        public bool RequireResultVerification { get; set; }

        public TeamWinCondition WinCondition { get; set; }

        /// <summary>
        /// How each individual sub-match series is settled. Distinct from <see cref="WinCondition"/>,
        /// which settles the tie itself over those sub-matches.
        /// </summary>
        public TeamWinCondition SeriesWinCondition { get; set; }

        public TeamMatchTeamProjection? HomeTeam { get; set; }
        public TeamMatchTeamProjection? AwayTeam { get; set; }
        public List<SubMatchProjection> SubMatches { get; set; } = [];
    }

    public class TeamMatchTeamProjection
    {
        public Guid TeamId { get; set; }
        public string TeamName { get; set; } = "";
        public Guid? CaptainUserId { get; set; }
        public List<TeamMemberDto> Members { get; set; } = [];
    }

    public class SubMatchProjection
    {
        public Guid MatchId { get; set; }
        public int? MatchOrder { get; set; }
        public MatchStatus Status { get; set; }
        public int? HomeUserScore { get; set; }
        public int? AwayUserScore { get; set; }
        public Guid? WinnerParticipantId { get; set; }

        // The ready check runs per GAME of a tie, not per tie: each pairing agrees its own kick-off
        // and each player answers for himself, so a team-mate who does not turn up loses his own
        // game rather than the whole tie.
        public DateTime? ScheduledStartTime { get; set; }
        public DateTime? HomeCheckedInOn { get; set; }
        public DateTime? AwayCheckedInOn { get; set; }
        public DateTime? CheckInResolvedOn { get; set; }

        public Guid? HomeParticipantId { get; set; }
        public Guid? AwayParticipantId { get; set; }
        public Guid? HomeUserId { get; set; }
        public Guid? AwayUserId { get; set; }
        public string? HomeUsername { get; set; }
        public string? AwayUsername { get; set; }
        public string? HomeNickname { get; set; }
        public string? AwayNickname { get; set; }
        public string? HomeAvatarUrl { get; set; }
        public string? AwayAvatarUrl { get; set; }
        public List<string> Evidences { get; set; } = [];

        /// <summary>Same evidence, typed. See MatchEvidenceItemDto for why both exist.</summary>
        public List<MatchEvidenceItemDto> EvidenceItems { get; set; } = [];
        public int? ProposedHomeScore { get; set; }
        public int? ProposedAwayScore { get; set; }
        public Guid? ProposedByUserId { get; set; }
        public bool AdminHelpRequested { get; set; }
        public Guid? AdminHelpRequestedByUserId { get; set; }

        /// <summary>This game carries verification records — see MatchResultDetailDto.HasResultVerifications.</summary>
        public bool HasResultVerifications { get; set; }

        // Series format for this individual game, already resolved against the tournament default.
        public int BestOf { get; set; } = 1;
        public int? TiebreakBestOf { get; set; }

        // Real goals across the whole series. The tie's aggregate score is built from these, not
        // from HomeUserScore, which under MatchWins is a games-won tally.
        public int? HomeGoalsTotal { get; set; }
        public int? AwayGoalsTotal { get; set; }

        // Raw JSON straight off the column — EF can't deserialize in-query, so the service parses
        // these into the DTO's Games / ProposedGames lists after materialization.
        public string? GamesJson { get; set; }
        public string? ProposedGamesJson { get; set; }
    }

    public class TieBreakProjection
    {
        public Guid TeamMatchId { get; set; }
        public TeamMatchStatus Status { get; set; }
        public Guid? HomeTeamRepresentativeUserId { get; set; }
        public string? HomeRepresentativeUsername { get; set; }
        public Guid? AwayTeamRepresentativeUserId { get; set; }
        public string? AwayRepresentativeUsername { get; set; }
    }
}
