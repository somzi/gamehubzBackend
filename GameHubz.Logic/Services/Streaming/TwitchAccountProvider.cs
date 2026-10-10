using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameHubz.DataModels.Config;
using GameHubz.DataModels.Enums;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Twitch as the channel owner (user token from the authorization-code flow): the stream key, the
    /// stream title and the live check. Uses the same Twitch application as the VOD resolver
    /// (Streaming:Twitch:ClientId / ClientSecret) — it only needs the redirect uri registered.
    /// </summary>
    public class TwitchAccountProvider : IStreamAccountProvider
    {
        // Reading the key is the whole point; managing the broadcast is what lets us set the title.
        private const string Scopes = "channel:read:stream_key channel:manage:broadcast";
        private const string IngestCacheKey = "stream:twitch:ingestServer";
        private const string IngestListUrl = "https://ingest.twitch.tv/ingests";

        private readonly IHttpClientFactory httpClientFactory;
        private readonly IDistributedCache cache;
        private readonly StreamingPlatformConfig config;
        private readonly ILogger<TwitchAccountProvider> logger;

        public TwitchAccountProvider(
            IHttpClientFactory httpClientFactory,
            IDistributedCache cache,
            IOptions<StreamingConfig> options,
            ILogger<TwitchAccountProvider> logger)
        {
            this.httpClientFactory = httpClientFactory;
            this.cache = cache;
            this.config = options.Value.Twitch;
            this.logger = logger;
        }

        public SocialType Platform => SocialType.Twitch;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(config.ClientId) &&
            !string.IsNullOrWhiteSpace(config.ClientSecret) &&
            !string.IsNullOrWhiteSpace(config.RedirectUri);

        public bool UsesPkce => false;

        public string BuildAuthorizeUrl(string state, string? codeChallenge)
        {
            return "https://id.twitch.tv/oauth2/authorize"
                + $"?client_id={Uri.EscapeDataString(config.ClientId)}"
                + $"&redirect_uri={Uri.EscapeDataString(config.RedirectUri)}"
                + "&response_type=code"
                + $"&scope={Uri.EscapeDataString(Scopes)}"
                + $"&state={Uri.EscapeDataString(state)}"
                // Always show the consent screen, so a phone signed into someone else's Twitch in the
                // browser can't link that account by accident.
                + "&force_verify=true";
        }

        public Task<StreamOAuthTokens?> ExchangeCodeAsync(string code, string? codeVerifier, CancellationToken ct)
        {
            return StreamOAuthJson.PostTokenAsync(Client(), "https://id.twitch.tv/oauth2/token", new Dictionary<string, string>
            {
                ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = config.RedirectUri,
            }, logger, "Twitch", ct);
        }

        public Task<StreamOAuthTokens?> RefreshAsync(string refreshToken, CancellationToken ct)
        {
            return StreamOAuthJson.PostTokenAsync(Client(), "https://id.twitch.tv/oauth2/token", new Dictionary<string, string>
            {
                ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            }, logger, "Twitch", ct);
        }

        public async Task<StreamChannelIdentity?> GetIdentityAsync(string accessToken, CancellationToken ct)
        {
            using var response = await Api(() => HelixRequest(HttpMethod.Get, "https://api.twitch.tv/helix/users", accessToken), ct);
            using var doc = await StreamOAuthJson.ReadJsonAsync(response, ct);
            if (doc == null || StreamOAuthJson.FirstData(doc) is not JsonElement user) return null;

            string? id = StreamOAuthJson.GetString(user, "id");
            string? login = StreamOAuthJson.GetString(user, "login");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(login)) return null;

            return new StreamChannelIdentity(id, login, StreamOAuthJson.GetString(user, "display_name"));
        }

        public async Task<StreamIngest?> GetIngestAsync(string accessToken, string externalUserId, CancellationToken ct)
        {
            using var response = await Api(() => HelixRequest(HttpMethod.Get,
                $"https://api.twitch.tv/helix/streams/key?broadcaster_id={Uri.EscapeDataString(externalUserId)}", accessToken), ct);
            using var doc = await StreamOAuthJson.ReadJsonAsync(response, ct);
            if (doc == null || StreamOAuthJson.FirstData(doc) is not JsonElement data)
            {
                logger.LogWarning("Twitch stream key request returned {Status}.", (int)response.StatusCode);
                return null;
            }

            string? key = StreamOAuthJson.GetString(data, "stream_key");
            if (string.IsNullOrEmpty(key)) return null;

            string? server = await GetIngestServerAsync(ct);
            return server == null ? null : new StreamIngest(server, key);
        }

        public async Task<bool> TrySetTitleAsync(string accessToken, string externalUserId, string title, CancellationToken ct)
        {
            using var response = await Api(() =>
            {
                var request = HelixRequest(HttpMethod.Patch,
                    $"https://api.twitch.tv/helix/channels?broadcaster_id={Uri.EscapeDataString(externalUserId)}", accessToken);
                request.Content = JsonContent.Create(new { title });
                return request;
            }, ct);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool?> IsLiveAsync(string accessToken, string externalUserId, CancellationToken ct)
        {
            using var response = await Api(() => HelixRequest(HttpMethod.Get,
                $"https://api.twitch.tv/helix/streams?user_id={Uri.EscapeDataString(externalUserId)}", accessToken), ct);
            using var doc = await StreamOAuthJson.ReadJsonAsync(response, ct);
            if (doc == null) return null;

            return StreamOAuthJson.FirstData(doc) is JsonElement stream
                && StreamOAuthJson.GetString(stream, "type") == "live";
        }

        public async Task RevokeAsync(string? accessToken, string? refreshToken, CancellationToken ct)
        {
            // Twitch revokes the whole grant through the access token.
            if (string.IsNullOrEmpty(accessToken)) return;

            using var response = await Client().PostAsync("https://id.twitch.tv/oauth2/revoke",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = config.ClientId,
                    ["token"] = accessToken,
                }), ct);
        }

        // The ingest list is ordered by distance from whoever asks — here, the API server. Players are
        // close enough to it for that to be a fine pick; a fixed server in config overrides it.
        private async Task<string?> GetIngestServerAsync(CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(config.IngestServer)) return config.IngestServer.Trim();

            string? cached = await cache.GetStringAsync(IngestCacheKey, ct);
            if (!string.IsNullOrWhiteSpace(cached)) return cached;

            using var response = await Api(() => new HttpRequestMessage(HttpMethod.Get, IngestListUrl), ct);
            using var doc = await StreamOAuthJson.ReadJsonAsync(response, ct);
            if (doc == null || !doc.RootElement.TryGetProperty("ingests", out var ingests)) return null;

            foreach (var ingest in ingests.EnumerateArray())
            {
                string? template = StreamOAuthJson.GetString(ingest, "url_template_secure")
                    ?? StreamOAuthJson.GetString(ingest, "url_template");
                if (string.IsNullOrWhiteSpace(template)) continue;

                // "rtmps://xyz.contribute.live-video.net:443/app/{stream_key}" → the application url.
                string server = template.Replace("{stream_key}", string.Empty).TrimEnd('/');
                await cache.SetStringAsync(IngestCacheKey, server,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6) }, ct);
                return server;
            }

            return null;
        }

        private HttpRequestMessage HelixRequest(HttpMethod method, string url, string accessToken)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Add("Client-Id", config.ClientId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return request;
        }

        private HttpClient Client() => httpClientFactory.CreateClient("StreamOAuth");

        private Task<HttpResponseMessage> Api(Func<HttpRequestMessage> makeRequest, CancellationToken ct) =>
            StreamOAuthJson.SendApiAsync(Client(), makeRequest, "Twitch", ct);
    }
}
