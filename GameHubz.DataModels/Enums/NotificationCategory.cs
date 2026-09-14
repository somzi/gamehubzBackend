namespace GameHubz.DataModels.Enums
{
    /// <summary>
    /// Which inbox filter a notification belongs to. Decided on the server when the row is written,
    /// so the tabs filter and count the whole history rather than whatever page the app has loaded.
    /// </summary>
    public enum NotificationCategory
    {
        /// <summary>For information — something happened.</summary>
        Update = 0,

        /// <summary>Asks the recipient to do something: confirm, schedule, answer, approve.</summary>
        Action = 1,
    }
}
