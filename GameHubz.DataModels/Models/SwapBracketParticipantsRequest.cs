namespace GameHubz.DataModels.Models
{
    // Admin manual re-seed: exchange the bracket positions of two first-round participants
    // (teams or players) before their matches are played.
    public class SwapBracketParticipantsRequest
    {
        public Guid ParticipantAId { get; set; }
        public Guid ParticipantBId { get; set; }
    }
}
