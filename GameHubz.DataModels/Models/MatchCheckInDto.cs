namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// The ready-check state of one match, as both sides' clients render it: who has confirmed,
    /// when the button lights up, and when the side still missing loses the match.
    /// </summary>
    public class MatchCheckInDto
    {
        public Guid MatchId { get; set; }

        /// <summary>False when the tournament runs no ready check — every other field is then null.</summary>
        public bool RequireMatchCheckIn { get; set; }

        /// <summary>Resolved grace window in minutes (never the raw null the organizer may have left).</summary>
        public int GraceMinutes { get; set; }

        public DateTime? ScheduledStartTime { get; set; }
        public DateTime? CheckInOpensAt { get; set; }
        public DateTime? CheckInDeadline { get; set; }
        public DateTime? HomeCheckedInOn { get; set; }
        public DateTime? AwayCheckedInOn { get; set; }

        /// <summary>
        /// Which side the caller plays: true = home, false = away, null = neither (an organizer or
        /// a spectator watching the same screen). Clients use it to decide whose button this is.
        /// </summary>
        public bool? IsHome { get; set; }

        /// <summary>Set once the check has been ruled on — a forfeit or a double no-show.</summary>
        public DateTime? ResolvedOn { get; set; }
    }
}
