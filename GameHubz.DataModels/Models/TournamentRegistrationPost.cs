using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Interfaces;

namespace GameHubz.DataModels.Models
{
    public class TournamentRegistrationPost : IEditableDto
    {
        public Guid? Id { get; set; }

        public Guid? TournamentId { get; set; }

        public Guid? UserId { get; set; }

        public Guid? TeamId { get; set; }

        public TournamentRegistrationStatus Status { get; set; }

        /// <summary>
        /// Join code of a private tournament (see TournamentEntity.IsPrivate). Required for a solo
        /// sign-up to one; never stored — it is checked and dropped. Ignored for public tournaments,
        /// so clients that predate the feature keep working everywhere else.
        /// </summary>
        public string? JoinCode { get; set; }
    }
}