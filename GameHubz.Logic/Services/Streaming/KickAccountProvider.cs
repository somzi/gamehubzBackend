using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameHubz.DataModels.Config;
using GameHubz.DataModels.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Kick's official public API as the channel owner (OAuth 2.1 with PKCE). The channel object carries
    /// the stream url + key (streamkey:read) and the live flag; channel:write sets the title. VODs are
    /// still not on the official API — KickStreamClient keeps resolving those.
    /// </summary>
    public class KickAccountProvider : IStreamAccountProvider
    {
        private const string Scopes = "user:read channel:read channel:write streamkey:read";
        private const string ChannelsUrl = "https://api.kick.com/public/v1/channels";

        private readonly IHttpClientFactory httpClientFactory;
        private readonly StreamingPlatformConfig config;
        private readonly ILogger<KickAccountProvider> logger;

        public KickAccountProvider(
            IHttpClientFactory httpClientFactory,
            IOptions<StreamingConfig> options,
            ILogger<KickAccountProvider> logger)
        {
            this.httpClientFactory = httpClientFactory;
            this.config = options.Value.Kick;
            this.logger = logger;
        }

        public SocialType Platform => SocialType.Kick;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(config.ClientId) &&
            !string.IsNullOrWhiteSpace(config.ClientSecret) &&
            !string.IsNullOrWhiteSpace(config.RedirectUri);

        public bool UsesPkce => true;

        public string BuildAuthorizeUrl(string state, string? codeChallenge)
        {
            return "https://id.kick.com/oauth/authorize"
                + "?response_type=code"
                + $"&client_id={Uri.EscapeDataString(config.ClientId)}"
                + $"&redirect_uri={Uri.EscapeDataString(config.RedirectUri)}"
                + $"&scope={Uri.EscapeDataString(Scopes)}"
                + $"&code_challenge={Uri.EscapeDataString(codeChallenge ?? string.Empty)}"
                + "&code_challenge_method=S256"
                + $"&state={Uri.EscapeDataString(state)}";
        }

        public Task<StreamOAuthTokens?> ExchangeCodeAsync(string code, string? codeVerifier, CancellationToken ct)
        {
            return StreamOAuthJson.PostTokenAsync(Client(), "https://id.kick.com/oauth/token", new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret,
                ["redirect_uri"] = config.RedirectUri,
                ["code_verifier"] = codeVerifier ?? string.Empty,
            }, logger, "Kick", ct);
        }

        public Task<StreamOAuthTokens?> RefreshAsync(string refreshToken, CancellationToken ct)
        {
            return StreamOAuthJson.PostTokenAsync(Client(), "https://id.kick.com/oauth/token", new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret,
            }, logger, "Kick", ct);
        }

        public async Task<StreamChannelIdentity?> GetIdentityAsync(string accessToken, CancellationToken ct)
        {
            JsonElement? channel = await GetOwnChannelAsync(accessToken, ct);
            if (channel is not JsonElement c) return null;

            string? id = StreamOAuthJson.GetString(c, "broadcaster_user_id");
            string? slug = StreamOAuthJson.GetString(c, "slug");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(slug)) return null;

            // The display name lives on the user, not the channel. Nice to have, never required.
            string? name = null;
            try
            {
                using var response = await Api(() => ApiRequest(HttpMethod.Get, "https://api.kick.com/public/v1/users", accessToken), ct);
                using var doc = await StreamOAuthJson.ReadJsonAsync(response, ct);
                if (doc != null && StreamOAuthJson.FirstData(doc) is JsonElement user)
                    name = StreamOAuthJson.GetString(user, "name");
            }
            catch (Exception ex) when (ex is StreamPlatformUnavailableException or StreamTokenRejectedException or JsonException)
            {
                logger.LogInformation(ex, "Kick user name lookup failed; falling back to the slug.");
            }

            return new StreamChannelIdentity(id, slug, name ?? slug);
        }

        public async Task<StreamIngest?> GetIngestAsync(string accessToken, string externalUserId, CancellationToken ct)
        {
            JsonElement? channel = await GetOwnChannelAsync(accessToken, ct);
            if (channel is not JsonElement c || !c.TryGetProperty("stream", out var stream)) return null;

            string? url = StreamOAuthJson.GetString(stream, "url");
            string? key = StreamOAuthJson.GetString(stream, "key");
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            {
                logger.LogWarning("Kick channel response carried no stream url/key (is streamkey:read granted?).");
                return null;
            }

            return new StreamIngest(NormalizeServer(url), key);
        }

        public async Task<bool> TrySetTitleAsync(string accessToken, string externalUserId, string title, CancellationToken ct)
        {
            using var response = await Api(() =>
            {
                var request = ApiRequest(HttpMethod.Patch, ChannelsUrl, accessToken);
                request.Content = JsonContent.Create(new { stream_title = title });
                return request;
            }, ct);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool?> IsLiveAsync(string accessToken, string externalUserId, CancellationToken ct)
        {
            JsonElement? channel = await GetOwnChannelAsync(accessToken, ct);
            if (channel is not JsonElement c) return null;

            return c.TryGetProperty("stream", out var stream)
                && stream.TryGetProperty("is_live", out var live)
                && live.ValueKind == JsonValueKind.True;
        }

        public async Task RevokeAsync(string? accessToken, string? refreshToken, CancellationToken ct)
        {
            // Revoking the refresh token ends the grant; the access token dies with its short lifetime.
            string? token = refreshToken ?? accessToken;
            if (string.IsNullOrEmpty(token)) return;

            string hint = refreshToken != null ? "refresh_token" : "access_token";
            string url = $"https://id.kick.com/oauth/revoke?token={Uri.EscapeDataString(token)}&token_hint_type={hint}";
            using var response = await Client().PostAsync(url,
                new FormUrlEncodedContent(new Dictionary<string, string>()), ct);
        }

        // No parameters → the channel of the token's owner.
        private async Task<JsonElement?> GetOwnChannelAsync(string accessToken, CancellationToken ct)
        {
            using var response = await Api(() => ApiRequest(HttpMethod.Get, ChannelsUrl, accessToken), ct);
            using var doc = await StreamOAuthJson.ReadJsonAsync(response, ct);
            if (doc == null)
            {
                logger.LogWarning("Kick channels request returned {Status}.", (int)response.StatusCode);
                return null;
            }

            // Clone: the element must outlive the JsonDocument it came from.
            return StreamOAuthJson.FirstData(doc)?.Clone();
        }

        // Kick hands out the ingest host (rtmps://xxxx.global-contribute.live-video.net); the encoder
        // needs the application path on it as well.
        private static string NormalizeServer(string url)
        {
            string trimmed = url.Trim().TrimEnd('/');
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && (uri.AbsolutePath == "/" || uri.AbsolutePath.Length == 0))
                return trimmed + "/app";
            return trimmed;
        }

        private static HttpRequestMessage ApiRequest(HttpMethod method, string url, string accessToken)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }

        private HttpClient Client() => httpClientFactory.CreateClient("StreamOAuth");

        private Task<HttpResponseMessage> Api(Func<HttpRequestMessage> makeRequest, CancellationToken ct) =>
            StreamOAuthJson.SendApiAsync(Client(), makeRequest, "Kick", ct);
    }
}
