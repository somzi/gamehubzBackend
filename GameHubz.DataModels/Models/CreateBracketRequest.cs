namespace GameHubz.DataModels.Models
{
    public class CreateBracketRequest
    {
        public Guid TournamentId { get; set; }
        public int? GroupsCount { get; set; }
        public int? QualifiersPerGroup { get; set; }
    }
}