using GameHubz.DataModels.Interfaces;

namespace GameHubz.DataModels.Models
{
    public class TournamentParticipantPost : IEditableDto
    {
        public Guid? Id { get; set; }

        public Guid? TournamentId { get; set; }

        public Guid? UserId { get; set; }
    }
}