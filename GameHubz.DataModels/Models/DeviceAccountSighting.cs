namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// An account registered on a phone, and when it first appeared there (its earliest registration
    /// matching the phone by installation id or platform id hash).
    /// </summary>
    public record DeviceAccountSighting(Guid UserId, DateTime? FirstSeenOn);
}
