using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    public class TournamentPlayerDeviceEntity : BaseEntity
    {
        public Guid TournamentId { get; set; }
        public TournamentEntity? Tournament { get; set; }
        public Guid UserId { get; set; }
        public UserEntity? User { get; set; }
        public Guid UserDeviceId { get; set; }
        public UserDeviceEntity? UserDevice { get; set; }
        public TournamentPlayerDeviceStatus Status { get; set; }
        public DateTime RequestedOn { get; set; }
        public DateTime? DecidedOn { get; set; }
        public Guid? DecidedByUserId { get; set; }
    }
}
