using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    public class TournamentTeamMemberEntity : BaseEntity
    {
        public Guid? TeamId { get; set; }
        public TournamentTeamEntity? Team { get; set; }
        public Guid? UserId { get; set; }
        public UserEntity? User { get; set; }
        public DateTime? JoinedAt { get; set; }
    }
}
