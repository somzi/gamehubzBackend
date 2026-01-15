using GameHubz.Common;
using GameHubz.DataModels.Enums;

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
    }
}