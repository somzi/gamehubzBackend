namespace GameHubz.DataModels.Models
{
    // Minimal projection used by the solo group/league stats resync — keeps the wire
    // payload tiny and avoids EF change-tracking on read-only data.
    public class GroupMatchStatsRow
    {
        public Guid HomeParticipantId { get; set; }
        public Guid AwayParticipantId { get; set; }
        public int HomeScore { get; set; }
        public int AwayScore { get; set; }
        public Guid? WinnerParticipantId { get; set; }
    }
}
