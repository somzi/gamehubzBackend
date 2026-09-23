using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    /// <summary>
    /// A phone an account has verified match results from.
    ///
    /// Device binding, not identification: nothing here says who the person holding the phone is. The
    /// row carries the key the phone keeps behind its own biometrics (Face ID / Touch ID / Android
    /// BiometricPrompt) — the fingerprint or face never leaves the phone, and GameHubz only ever learns
    /// that the phone's owner unlocked the key. What the server CAN tell from these rows is whether a
    /// verification came from a phone the account has used before, or from one it has never seen.
    ///
    /// One row per account per phone. Two accounts verifying from the same installation share a
    /// <see cref="DeviceId"/> but each gets its own key — which is how a shared phone shows up.
    /// </summary>
    public class UserDeviceEntity : BaseEntity
    {
        public Guid UserId { get; set; }

        public UserEntity? User { get; set; }

        /// <summary>
        /// GameHubz installation id. Issued by the server on the phone's first registration and kept in
        /// the phone's secure storage (this-device-only, so a backup restored onto another phone does
        /// not carry it along). Reinstalling the app on Android clears it; the platform id hash below is
        /// what still ties the two installations together.
        /// </summary>
        public Guid DeviceId { get; set; }

        /// <summary>
        /// HMAC-SHA256 key (hex) issued to this phone for this account. The phone stores it behind
        /// biometrics, so answering a challenge with it requires a successful unlock. Handed out exactly
        /// once, at issue; never returned by any read.
        /// </summary>
        public string KeySecret { get; set; } = string.Empty;

        /// <summary>
        /// When the current key was issued. A key re-issued minutes before a verification — a new
        /// install, or biometrics changed on the phone — is worth a second look.
        /// </summary>
        public DateTime KeyIssuedOn { get; set; }

        /// <summary>"ios" or "android".</summary>
        public string Platform { get; set; } = string.Empty;

        public string? DeviceModel { get; set; }

        public string? DeviceBrand { get; set; }

        public string? OsVersion { get; set; }

        public string? AppVersion { get; set; }

        /// <summary>False for an emulator or simulator.</summary>
        public bool IsPhysicalDevice { get; set; }

        /// <summary>
        /// SHA-256 of the platform's own install id (iOS identifierForVendor, Android ANDROID_ID). Stored
        /// hashed: it is only ever compared, never shown. Survives an app reinstall on Android, so two
        /// "different" installations on the same phone can still be matched up.
        /// </summary>
        public string? PlatformDeviceIdHash { get; set; }

        /// <summary>When the app was installed on the phone, as the phone reports it.</summary>
        public DateTime? AppInstalledOn { get; set; }

        public DateTime LastSeenOn { get; set; }

        public DateTime? LastVerifiedOn { get; set; }
    }
}
