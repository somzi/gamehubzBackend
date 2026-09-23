using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// The slice of a tournament the result path needs, projected in one query: who may confirm a
    /// result, and the default series format a match inherits when it has no override of its own.
    /// </summary>
    public class TournamentApprovalContext
    {
        public Guid HubOwnerUserId { get; set; }
        public bool RequireResultApproval { get; set; }

        /// <summary>
        /// Ready check. The result path needs it to refuse a participant's report while the match
        /// is still waiting on a check-in — the whole point of the feature is that the score is
        /// entered by two players who both said they were there.
        /// </summary>
        public bool RequireMatchCheckIn { get; set; }

        /// <summary>Grace minutes for the ready check. Null = the system default.</summary>
        public int? CheckInGraceMinutes { get; set; }

        /// <summary>
        /// Result verification. The result path needs it to refuse a participant's report until that
        /// participant has a verified record for the match.
        /// </summary>
        public bool RequireResultVerification { get; set; }

        /// <summary>Tournament default Best-of. 1 for every tournament created before series existed.</summary>
        public int BestOf { get; set; } = 1;

        /// <summary>Games won vs total score — how a multi-game series is settled.</summary>
        public TeamWinCondition SeriesWinCondition { get; set; }

        /// <summary>Tiebreak replay format. Null = replay the match's own Best-of.</summary>
        public int? TiebreakBestOf { get; set; }

        /// <summary>
        /// Best-of for knockout matches of a two-phase tournament. Null = the knockout inherits
        /// <see cref="BestOf"/> like everything else.
        /// </summary>
        public int? KnockoutBestOf { get; set; }

        /// <summary>Needed to tell a two-phase tournament from a plain bracket, where the knockout override does not apply.</summary>
        public TournamentFormat Format { get; set; }
    }
}
