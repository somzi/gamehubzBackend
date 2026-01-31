namespace GameHubz.DataModels.Models
{
    public class MatchEvidenceEdit
    {
        public Guid? Id { get; set; }

        public Guid? MatchId { get; set; }

        public MatchEdit? Match { get; set; }


    }
}