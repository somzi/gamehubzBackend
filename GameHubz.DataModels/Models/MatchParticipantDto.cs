namespace GameHubz.DataModels.Models
{
    public class MatchParticipantDto
    {
        public Guid ParticipantId { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; } = "TBD";
        public int? Score { get; set; }
        public bool IsWinner { get; set; }
        public int? Seed { get; set; }
        public string? TeamName { get; set; }
        /// <summary>
        /// Profile photo of the player behind this slot, so bracket / group cards can draw the
        /// real avatar instead of falling back to the username initials. Null for team slots
        /// (they render a team icon) and for players who never uploaded one.
        /// </summary>
        public string? AvatarUrl { get; set; }
    }
}