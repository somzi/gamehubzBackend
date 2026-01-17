using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    public class TournamentRegistrationEntity : BaseEntity
    {
        public Guid? TournamentId { get; set; }

        public Guid? UserId { get; set; }

        public RegistrationStatus Status { get; set; }

        public TournamentEntity? Tournament { get; set; }

        public UserEntity? User { get; set; }
    }
}