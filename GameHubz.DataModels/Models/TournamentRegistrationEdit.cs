using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class TournamentRegistrationEdit
    {
        public Guid? Id { get; set; }

        public Guid? TournamentId { get; set; }

        public Guid? UserId { get; set; }

        public TournamentRegistrationStatus Status { get; set; }

        public TournamentEdit? Tournament { get; set; }

        public UserEdit? User { get; set; }
    }
}