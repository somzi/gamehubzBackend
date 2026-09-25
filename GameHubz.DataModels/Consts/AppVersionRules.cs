namespace GameHubz.DataModels.Consts
{
    /// <summary>
    /// The oldest app build the server still supports. Every older build that carries the version check
    /// shows a full-screen "update required" screen it cannot dismiss — the app asks
    /// <c>GET api/app/version-check</c> on launch and again when it comes back to the foreground.
    /// <para>
    /// Raise it only once the store build that satisfies it is live in BOTH stores; before that, users
    /// would be sent to update to a version they cannot download yet. OTA updates never need it: with
    /// runtimeVersion "appVersion" an update only reaches builds of the same version anyway.
    /// </para>
    /// </summary>
    public static class AppVersionRules
    {
        public const string MinSupportedAppVersion = "3.0.0";
    }
}
