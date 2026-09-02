namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// Sent by the mobile client whenever the in-app language changes (and once after login,
    /// so an existing account picks up the choice it already made on the device).
    /// <para>
    /// The stored value is what push notifications and e-mails are written in — those are read
    /// long after the request that triggered them, by someone who is not the caller, so the
    /// <c>Language</c> request header cannot answer for them.
    /// </para>
    /// </summary>
    public class UpdateLanguageRequest
    {
        public string? Language { get; set; }
    }
}
