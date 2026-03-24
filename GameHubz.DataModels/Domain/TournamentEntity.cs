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
        public int Prize { get; set; }
        public PrizeCurrency PrizeCurrency { get; set; }
        public RegionType Region { get; set; }
        public Guid? WinnerUserId { get; set; }
        public UserEntity? WinnerUser { get; set; }

        public bool IsTeamTournament { get; set; }
        public int? TeamSize { get; set; }
        public Guid? WinnerTeamId { get; set; }
        public TournamentTeamEntity? WinnerTeam { get; set; }

        public int? QualifiersPerGroup { get; set; }
        public int? GroupsCount { get; set; }
        public int? RoundDurationMinutes { get; set; }
        public List<TournamentRegistrationEntity>? TournamentRegistrations { get; set; } = new();
        public List<TournamentStageEntity>? TournamentStages { get; set; } = new();
        public List<TournamentParticipantEntity>? TournamentParticipants { get; set; } = new();
    }
}