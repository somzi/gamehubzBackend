using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    public class MatchEntity : BaseEntity
    {
        public Guid TournamentId { get; set; }

        public int? RoundNumber { get; set; }

        public Guid HomeUserId { get; set; }

        public Guid AwayUserId { get; set; }

        public int? HomeUserScore { get; set; }

        public int? AwayUserScore { get; set; }

        public DateTime? ScheduledStartTime { get; set; }

        public string? Status { get; set; }

        public Guid? WinnerUserId { get; set; }

        public TournamentEntity? Tournament { get; set; }

        public UserEntity? HomeUser { get; set; }

        public UserEntity? AwayUser { get; set; }

        public UserEntity? WinnerUser { get; set; }
    }
}