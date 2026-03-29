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
        public Guid? HomeParticipantId { get; set; }
        public Guid? AwayParticipantId { get; set; }
        public Guid? HomeUserId { get; set; }
        public Guid? AwayUserId { get; set; }
        public string? HomeUsername { get; set; }
        public string? AwayUsername { get; set; }
        public string? HomeAvatarUrl { get; set; }
        public string? AwayAvatarUrl { get; set; }
        public List<string> Evidences { get; set; } = [];
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
