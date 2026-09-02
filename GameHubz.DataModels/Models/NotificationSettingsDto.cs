namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// The signed-in user's notification switches. One field today, but shaped as an object so
    /// adding the next toggle does not mean another endpoint.
    /// </summary>
    public class NotificationSettingsDto
    {
        /// <summary>
        /// Notify me about match chats I only moderate. Off silences push, Discord DM and the
        /// badge for organizer threads; chats on the user's own matches are never affected.
        /// </summary>
        public bool ModeratedChatNotifications { get; set; } = true;
    }
}
