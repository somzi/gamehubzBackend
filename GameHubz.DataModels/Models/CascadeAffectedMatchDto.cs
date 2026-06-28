using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    // One already-played match that a cascade revert/edit would reopen, listed in the order it
    // would be reverted (deepest downstream first). Returned by the cascade preview so the client
    // can show exactly what will be undone before the user confirms. Names are resolved client-side
    // from the bracket structure it already holds; this payload stays cheap (no participant joins).
    public class CascadeAffectedMatchDto
    {
        public Guid MatchId { get; set; }
        public int Round { get; set; }
        public MatchStage Stage { get; set; }
        public bool IsUpperBracket { get; set; }
        public int HomeScore { get; set; }
        public int AwayScore { get; set; }
    }
}
