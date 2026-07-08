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
        public TeamMatchTeamInfoDto? HomeTeam { get; set; }
        public TeamMatchTeamInfoDto? AwayTeam { get; set; }
        public List<TeamSubMatchDto> SubMatches { get; set; } = new();
        public TeamAggregateScoreDto? AggregateScore { get; set; }
        public TeamTieBreakInfoDto? TieBreak { get; set; }
        public bool RequireResultApproval { get; set; }
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
        public int? ProposedHomeScore { get; set; }
        public int? ProposedAwayScore { get; set; }
        public Guid? ProposedByUserId { get; set; }
        public bool AdminHelpRequested { get; set; }
        public Guid? AdminHelpRequestedByUserId { get; set; }
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
