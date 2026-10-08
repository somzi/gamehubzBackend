namespace GameHubz.DataModels.Models
{
    public class MatchChatAccessDto
    {
        /// <summary>
        /// The caller plays this match, the tournament keeps the chat shut until availability is set
        /// (TournamentEntity.RequireAvailabilityForChat), and the caller's side has not offered any
        /// hours yet. The app shows the way to the calendar instead of the conversation.
        /// </summary>
        public bool LockedUntilAvailability { get; set; }
    }
}
