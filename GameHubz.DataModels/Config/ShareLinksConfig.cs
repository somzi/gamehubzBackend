namespace GameHubz.DataModels.Config
{
    public class ShareLinksConfig
    {
        public string BaseUrl { get; set; } = "https://share.codespheresolutions.dev";

        public string AppScheme { get; set; } = "gamehubz";

        public string AppName { get; set; } = "GameHubz";

        public string? AppStoreUrl { get; set; }

        public string? PlayStoreUrl { get; set; }

        public string? DefaultImageUrl { get; set; }

        // Universal-link credentials; the /.well-known endpoints return 404 until these are filled in.
        public string? AppleTeamId { get; set; }

        public string IosBundleId { get; set; } = "com.codespheresolutions.gamehubzmobile";

        public string AndroidPackageName { get; set; } = "com.codespheresolutions.gamehubzmobile";

        public string[] AndroidCertFingerprints { get; set; } = [];
    }
}
