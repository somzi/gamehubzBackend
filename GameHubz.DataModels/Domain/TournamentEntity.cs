using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    public class TournamentEntity : BaseEntity
    {
        public Guid? HubId { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? Rules { get; set; }
        public TournamentStatus Status { get; set; }
        public int? MaxPlayers { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? RegistrationDeadline { get; set; }
        public TournamentFormat Format { get; set; }
        public HubEntity? Hub { get; set; }
        public List<TournamentRegistrationEntity>? TournamentRegistrations { get; set; } = new();
        public List<TournamentStageEntity>? TournamentStages { get; set; } = new();
        public List<TournamentParticipantEntity>? TournamentParticipants { get; set; } = new();
    }
}