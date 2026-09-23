namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// Account-local phone history for an installation. Android installations with the same reported
    /// platform id share this history, but keep separate keys and verification records.
    /// </summary>
    public record VerificationDeviceHistory(DateTime? FirstSeenOn, bool HasPreviousDevice, int AccountDeviceCount);
}
