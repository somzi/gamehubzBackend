using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    /// <summary>
    /// A Twitch or Kick channel the user linked through OAuth, so the app can broadcast the phone screen
    /// straight to it. Unlike the Discord link, the tokens are kept: every broadcast needs a fresh stream
    /// key, and only the channel owner's token can read it. Both tokens are stored encrypted
    /// (see StreamTokenProtector) and never leave the server.
    ///
    /// One row per user per platform, and one GameHubz account per channel.
    /// </summary>
    public class UserStreamAccountEntity : BaseEntity
    {
        public Guid UserId { get; set; }

        public UserEntity? User { get; set; }

        /// <summary>Twitch or Kick.</summary>
        public SocialType Platform { get; set; }

        /// <summary>The platform's own id for the channel: Twitch user id, Kick broadcaster_user_id.</summary>
        public string ExternalUserId { get; set; } = string.Empty;

        /// <summary>Channel handle the embeds are built from: Twitch login, Kick slug.</summary>
        public string Login { get; set; } = string.Empty;

        public string? DisplayName { get; set; }

        /// <summary>Encrypted access token.</summary>
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>Encrypted refresh token.</summary>
        public string RefreshToken { get; set; } = string.Empty;

        public DateTime AccessTokenExpiresOn { get; set; }

        /// <summary>Space-separated scopes the user granted.</summary>
        public string? Scopes { get; set; }

        /// <summary>
        /// The refresh token was refused (the user revoked access on the platform, changed the password,
        /// or the app's credentials changed). The account stays listed so the app can ask for a re-link.
        /// </summary>
        public bool NeedsRelink { get; set; }

        public DateTime LinkedOn { get; set; }
    }
}
