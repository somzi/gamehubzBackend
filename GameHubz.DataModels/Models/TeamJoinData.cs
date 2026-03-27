namespace GameHubz.DataModels.Models
{
    public class TeamJoinData
    {
        public Guid TeamId { get; set; }
        public Guid TournamentId { get; set; }
        public Guid CaptainUserId { get; set; }
        public string TeamName { get; set; } = "";
        public int? TeamSize { get; set; }
        public int CurrentMemberCount { get; set; }
        public bool UserAlreadyInTournament { get; set; }
        public List<TeamMemberDto> Members { get; set; } = [];
    }
}
