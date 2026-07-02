namespace GameHubz.DataModels.Config
{
    /// <summary>
    /// Discord application credentials for the HTTP-only bot (phase 2): OAuth account linking,
    /// personal DM notifications and slash commands over the interactions webhook. No gateway /
    /// websocket connection is ever opened. Empty values switch the corresponding feature off.
    /// </summary>
    public class DiscordConfig
    {
        /// <summary>Application id — used for slash-command registration.</summary>
        public string ApplicationId { get; set; } = "";

        /// <summary>Hex-encoded Ed25519 public key used to verify interaction signatures.</summary>
        public string PublicKey { get; set; } = "";

        /// <summary>Bot token ("Bot xxx" auth) for DM sending and command registration.</summary>
        public string BotToken { get; set; } = "";

        /// <summary>OAuth client id for the account-link flow.</summary>
        public string ClientId { get; set; } = "";

        /// <summary>OAuth client secret — server-side only, never leaves this process.</summary>
        public string ClientSecret { get; set; } = "";

        /// <summary>Backend callback URL registered with Discord (…/api/discord/link/callback).</summary>
        public string RedirectUri { get; set; } = "";

        /// <summary>Idempotently (re-)register the slash commands on startup.</summary>
        public bool RegisterCommandsOnStartup { get; set; } = false;
    }
}
