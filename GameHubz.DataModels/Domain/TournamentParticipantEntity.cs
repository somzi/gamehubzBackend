using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    public class TournamentParticipantEntity : BaseEntity
    {
        public Guid? TournamentId { get; set; }
        public TournamentEntity? Tournament { get; set; }
        public Guid? UserId { get; set; }
        public UserEntity? User { get; set; }
        public int? Seed { get; set; }
        public int? GroupPosition { get; set; }
        public int Points { get; set; } = 0;
        public int Wins { get; set; } = 0;
        public int Losses { get; set; } = 0;
        public int Draws { get; set; } = 0;
        public int GoalsFor { get; set; } = 0;
        public int GoalsAgainst { get; set; } = 0;
        public Guid? TournamentGroupId { get; set; }
        public TournamentGroupEntity? TournamentGroup { get; set; }
    }
}