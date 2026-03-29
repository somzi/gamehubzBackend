using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    public class TeamJoinRequestEntity : BaseEntity
    {
        public Guid? TeamId { get; set; }
        public TournamentTeamEntity? Team { get; set; }
        public Guid? UserId { get; set; }
        public UserEntity? User { get; set; }
        public JoinRequestStatus Status { get; set; }
    }
}
