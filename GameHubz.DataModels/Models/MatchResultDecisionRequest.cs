namespace GameHubz.DataModels.Models
{
    public class MatchResultDecisionRequest
    {
        public Guid MatchId { get; set; }

        // Opt-in for delete (revert): also revert every already-played match downstream of this one
        // (next round and loser-bracket drop, transitively) so the result can be removed. Defaults
        // to false, preserving the original "blocked with a lock message" behaviour for old clients.
        public bool Cascade { get; set; }
    }
}
