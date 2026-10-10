namespace GameHubz.DataModels.Config
{
    /// <summary>
    /// The "Streaming" section. The VOD resolvers read their own keys straight off IConfiguration
    /// (Streaming:Twitch:ClientId …); this typed view adds what the in-app broadcast needs: the OAuth
    /// account link (redirect URIs), the key the stored tokens are encrypted with, and the broadcast
    /// session timings. Empty credentials switch the account link for that platform off.
    /// </summary>
    public class StreamingConfig
    {
        public StreamingPlatformConfig Twitch { get; set; } = new();

        public StreamingPlatformConfig Kick { get; set; } = new();

        /// <summary>
        /// Base64 of a 32-byte key (AES-256-GCM) for the OAuth tokens stored on UserStreamAccount.
        /// Keep it out of appsettings: set it through the environment (Streaming__TokenEncryptionKey).
        /// Changing it makes every linked account need a re-link.
        /// </summary>
        public string TokenEncryptionKey { get; set; } = "";

        public StreamBroadcastConfig Broadcast { get; set; } = new();
    }

    public class StreamingPlatformConfig
    {
        public string ClientId { get; set; } = "";

        /// <summary>Server-side only, never leaves this process.</summary>
        public string ClientSecret { get; set; } = "";

        /// <summary>Backend callback registered with the platform (…/api/StreamAccount/{platform}/link/callback).</summary>
        public string RedirectUri { get; set; } = "";

        /// <summary>
        /// Optional fixed ingest server (rtmps://…/app). Twitch only: when empty the server asks
        /// ingest.twitch.tv for the closest one. Kick always hands out its own per-channel url.
        /// </summary>
        public string IngestServer { get; set; } = "";
    }

    public class StreamBroadcastConfig
    {
        /// <summary>How often the phone reports that it is still sending.</summary>
        public int HeartbeatIntervalSeconds { get; set; } = 30;

        /// <summary>No heartbeat for this long and the platform says the channel is off → the stream is ended.</summary>
        public int HeartbeatTimeoutSeconds { get; set; } = 120;

        /// <summary>No heartbeat for this long → ended, whatever the platform says.</summary>
        public int HeartbeatHardTimeoutMinutes { get; set; } = 10;

        /// <summary>After the "match is over" reminder, how long before the phone is told to stop.</summary>
        public int StopAfterMatchMinutes { get; set; } = 15;

        /// <summary>How long a prepared session waits for the phone to connect.</summary>
        public int PendingSessionMinutes { get; set; } = 15;
    }
}
