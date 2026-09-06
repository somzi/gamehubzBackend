namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// Round-wide double walkover. A round is scoped to its STAGE — Winners-Bracket round 2 and
    /// Losers-Bracket round 2 share a round number but are different rounds — exactly as the
    /// round-schedule editor scopes a deadline. See BracketService.ApplyRoundWalkover for which
    /// fixtures of the round actually qualify.
    /// </summary>
    public class RoundWalkoverRequest
    {
        public Guid StageId { get; set; }

        public int RoundNumber { get; set; }
    }
}
