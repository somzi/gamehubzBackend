using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    public class TournamentTeamEntity : BaseEntity
    {
        public Guid? TournamentId { get; set; }
        public TournamentEntity? Tournament { get; set; }
        public string TeamName { get; set; } = "";
        public Guid? CaptainUserId { get; set; }
        public UserEntity? CaptainUser { get; set; }
        public Guid? TournamentParticipantId { get; set; }
        public TournamentParticipantEntity? TournamentParticipant { get; set; }
        public bool RequiresApproval { get; set; }
        public List<TournamentTeamMemberEntity> Members { get; set; } = new();
        public List<TeamJoinRequestEntity> JoinRequests { get; set; } = new();
    }
}
