namespace GameHubz.DataModels.Models
{
    public class SetRoundDeadlineRequest
    {
        public int RoundNumber { get; set; }
        public DateTime? Deadline { get; set; }
        public DateTime? RoundStart { get; set; }

        // Scopes the schedule to a single stage. In double-elimination the Winners and Losers
        // brackets are separate stages whose rounds share the same RoundNumber, so without this
        // a round-2 deadline would land on both brackets. Null = legacy tournament-wide behavior
        // (kept for older clients and single-stage formats).
        public Guid? StageId { get; set; }

        // A null RoundStart/Deadline means "keep the existing value" (legacy contract, older
        // clients rely on it), so clearing needs an explicit signal. ClearRoundStart nulls
        // RoundOpenAt (= round is open immediately); ClearDeadline nulls RoundDeadline
        // (= no deadline, matches are played whenever). Old clients omit both → false.
        public bool ClearRoundStart { get; set; }
        public bool ClearDeadline { get; set; }
    }
}