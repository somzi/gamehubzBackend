using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// One per platform the app can broadcast to (Twitch, Kick): the OAuth account link plus the calls a
    /// broadcast needs made as the channel owner. Implementations return null / false for any platform
    /// failure instead of throwing — the caller decides what the user is told.
    /// </summary>
    public interface IStreamAccountProvider
    {
        SocialType Platform { get; }

        /// <summary>Client id, secret and redirect uri are all set.</summary>
        bool IsConfigured { get; }

        /// <summary>The authorize call carries a PKCE challenge (Kick requires it).</summary>
        bool UsesPkce { get; }

        string BuildAuthorizeUrl(string state, string? codeChallenge);

        Task<StreamOAuthTokens?> ExchangeCodeAsync(string code, string? codeVerifier, CancellationToken ct);

        /// <summary>Null when the platform refused the refresh token — the account needs a re-link.</summary>
        Task<StreamOAuthTokens?> RefreshAsync(string refreshToken, CancellationToken ct);

        Task<StreamChannelIdentity?> GetIdentityAsync(string accessToken, CancellationToken ct);

        /// <summary>Where the phone sends the stream, and the channel's key.</summary>
        Task<StreamIngest?> GetIngestAsync(string accessToken, string externalUserId, CancellationToken ct);

        Task<bool> TrySetTitleAsync(string accessToken, string externalUserId, string title, CancellationToken ct);

        /// <summary>Null when the platform could not be asked.</summary>
        Task<bool?> IsLiveAsync(string accessToken, string externalUserId, CancellationToken ct);

        /// <summary>Best effort: withdraws the grant on the platform's side when the user unlinks.</summary>
        Task RevokeAsync(string? accessToken, string? refreshToken, CancellationToken ct);
    }

    public sealed record StreamOAuthTokens(string AccessToken, string RefreshToken, int ExpiresInSeconds, string? Scopes);

    public sealed record StreamChannelIdentity(string ExternalUserId, string Login, string? DisplayName);

    /// <summary>ServerUrl is the rtmp(s) application url (…/app); the key is appended by the encoder.</summary>
    public sealed record StreamIngest(string ServerUrl, string StreamKey);
}
