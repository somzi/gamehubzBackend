namespace GameHubz.DataModels.Consts
{
    /// <summary>
    /// How long an inbox notification is kept, in one place: the retention sweep deletes by it and the
    /// inbox page reports it, so the app's "notifications are kept for N days" footer can never quote a
    /// number the server is not actually using.
    /// </summary>
    public static class NotificationRetentionRules
    {
        public const string RetentionDaysConfigKey = "Notifications:RetentionDays";

        public const int DefaultRetentionDays = 60;

        public static int ResolveRetentionDays(int? configured)
            => Math.Max(1, configured ?? DefaultRetentionDays);
    }
}
