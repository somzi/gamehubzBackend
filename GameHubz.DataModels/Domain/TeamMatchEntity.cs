using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    public class TeamMatchEntity : BaseEntity
    {
        public Guid TournamentId { get; set; }
        public TournamentEntity? Tournament { get; set; }
        public Guid? TournamentStageId { get; set; }
        public TournamentStageEntity? TournamentStage { get; set; }
        public Guid? HomeTeamParticipantId { get; set; }
        public TournamentParticipantEntity? HomeTeamParticipant { get; set; }
        public Guid? AwayTeamParticipantId { get; set; }
        public TournamentParticipantEntity? AwayTeamParticipant { get; set; }
        public Guid? HomeTeamRepresentativeUserId { get; set; }
        public Guid? AwayTeamRepresentativeUserId { get; set; }
        public int? RoundNumber { get; set; }
        public int? MatchOrder { get; set; }
        public TeamMatchStatus Status { get; set; }
        public Guid? WinnerTeamParticipantId { get; set; }
        public Guid? NextTeamMatchId { get; set; }
        public TeamMatchEntity? NextTeamMatch { get; set; }
        public List<MatchEntity> SubMatches { get; set; } = new();
    }
}
