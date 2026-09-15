namespace GameHubz.DataModels.Models
{
    public class AppVersionCheckDto
    {
        /// <summary>Oldest app version still supported (major.minor.patch). Anything older must update.</summary>
        public string MinSupportedVersion { get; set; } = "";

        /// <summary>App Store page the update screen opens on iOS; null when not configured.</summary>
        public string? IosStoreUrl { get; set; }

        /// <summary>Google Play page the update screen opens on Android; null when not configured.</summary>
        public string? AndroidStoreUrl { get; set; }
    }
}
