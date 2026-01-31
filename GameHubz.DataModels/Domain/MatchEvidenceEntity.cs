using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    public class MatchEvidenceEntity : BaseEntity
    {
        public string? Url { get; set; }

        public Guid? MatchId { get; set; }

        public MatchEntity? Match { get; set; }
    }
}