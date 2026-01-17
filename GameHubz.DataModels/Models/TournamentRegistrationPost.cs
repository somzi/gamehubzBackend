using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Interfaces;

namespace GameHubz.DataModels.Models
{
    public class TournamentRegistrationPost : IEditableDto
    {
        public Guid? Id { get; set; }

        public Guid? TournamentId { get; set; }

        public Guid? UserId { get; set; }

        public RegistrationStatus Status { get; set; }
    }
}