using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    public class TournamentRegistrationEntity : BaseEntity
    {
        public Guid? TournamentId { get; set; }

        public Guid? UserId { get; set; }

        public string? Status { get; set; }

        public TournamentEntity? Tournament { get; set; }

        public UserEntity? User { get; set; }
    }
}