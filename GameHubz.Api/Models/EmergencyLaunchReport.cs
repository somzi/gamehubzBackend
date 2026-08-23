namespace GameHubz.Api.Models
{
    /// <summary>
    /// What the mobile app reports when <c>expo-updates</c> had to fall back to the build's
    /// embedded bundle because a downloaded OTA update failed to boot. Every field is
    /// optional — the client sends whatever the updates runtime could tell it, and a report
    /// with nothing but a null reason is still worth a row.
    /// </summary>
    public class EmergencyLaunchReport
    {
        /// <summary>Updates.emergencyLaunchReason — free text from the updates runtime.</summary>
        public string? Reason { get; set; }

        /// <summary>Id of the update that was running, when one is known.</summary>
        public string? UpdateId { get; set; }

        /// <summary>Runtime version the client is on — tells you which builds to republish for.</summary>
        public string? RuntimeVersion { get; set; }

        /// <summary>EAS channel the client is pulling updates from (production / preview / ...).</summary>
        public string? Channel { get; set; }
    }
}
