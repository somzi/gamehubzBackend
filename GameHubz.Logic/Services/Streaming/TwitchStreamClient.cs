using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using GameHubz.DataModels.Enums;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GameHubz.Logic.Services
{
    // Twitch Helix with an app access token (client_credentials) — no per-user OAuth.
    // Live status + VOD archive are public. App token is cached in Redis.
    // NOTE: Twitch auto-deletes VODs after 14 days (60 for affiliates/partners) and only if the
    // streamer has VOD storage enabled — so the resolved link can stop working over time.
    public class TwitchStreamClient : IStreamPlatformClient
    {
        public SocialType Platform => SocialType.Twitch;

        private const string TokenCacheKey = "stream:twitch:appToken";

        private readonly IHttpClientFactory httpClientFactory;
        private readonly IConfiguration config;
        private readonly IDistributedCache cache;
        private readonly ILogger<TwitchStreamClient> logger;

        public TwitchStreamClient(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            IDistributedCache cache,
            ILogger<TwitchStreamClient> logger)
        {
            this.httpClientFactory = httpClientFactory;
            this.config = config;
            this.cache = cache;
            this.logger = logger;
        }

        public async Task<string?> TryResolveVodUrlAsync(
            string handle,
            DateTime startedAtUtc,
            DateTime endedAtUtc,
            CancellationToken cancellationToken = default)
        {
            var clientId = this.config["Streaming:Twitch:ClientId"];
            var clientSecret = this.config["Streaming:Twitch:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                this.logger.LogWarning("Twitch credentials are not configured (Streaming:Twitch).");
                return null;
            }

            var login = NormalizeLogin(handle);
            if (string.IsNullOrWhiteSpace(login)) return null;

            var token = await GetAppTokenAsync(clientId, clientSecret, cancellationToken);
            if (token == null) return null;

            var client = this.httpClientFactory.CreateClient();

            // 1) resolve the numeric user id from the login name
            var userId = await GetUserIdAsync(client, clientId, token, login, cancellationToken);
            if (string.IsNullOrWhiteSpace(userId)) return null;

            // 2) the latest archive (= past broadcast / VOD) is the one we just finished
            using var req = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.twitch.tv/helix/videos?user_id={Uri.EscapeDataString(userId)}&type=archive&first=5&sort=time");
            req.Headers.Add("Client-Id", clientId);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await client.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode) return null;

            var data = await resp.Content.ReadFromJsonAsync<TwitchVideosResponse>(cancellationToken: cancellationToken);
            var videos = data?.Data;
            if (videos == null || videos.Count == 0) return null;

            // Prefer an archive created around the stream window; otherwise take the newest.
            var windowStart = startedAtUtc.AddHours(-2);
            var best = videos
                .Where(v => v.CreatedAt >= windowStart)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefault()
                ?? videos.OrderByDescending(v => v.CreatedAt).FirstOrDefault();

            return string.IsNullOrWhiteSpace(best?.Url) ? null : best!.Url;
        }

        private async Task<string?> GetUserIdAsync(
            HttpClient client, string clientId, string token, string login, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.twitch.tv/helix/users?login={Uri.EscapeDataString(login)}");
            req.Headers.Add("Client-Id", clientId);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var data = await resp.Content.ReadFromJsonAsync<TwitchUsersResponse>(cancellationToken: ct);
            return data?.Data?.FirstOrDefault()?.Id;
        }

        private async Task<string?> GetAppTokenAsync(string clientId, string clientSecret, CancellationToken ct)
        {
            var cached = await this.cache.GetStringAsync(TokenCacheKey, ct);
            if (!string.IsNullOrWhiteSpace(cached)) return cached;

            var client = this.httpClientFactory.CreateClient();
            var url = "https://id.twitch.tv/oauth2/token" +
                      $"?client_id={Uri.EscapeDataString(clientId)}" +
                      $"&client_secret={Uri.EscapeDataString(clientSecret)}" +
                      "&grant_type=client_credentials";

            using var resp = await client.PostAsync(url, null, ct);
            if (!resp.IsSuccessStatusCode)
            {
                this.logger.LogWarning("Twitch token request failed: {Status}", resp.StatusCode);
                return null;
            }

            var token = await resp.Content.ReadFromJsonAsync<TwitchTokenResponse>(cancellationToken: ct);
            if (string.IsNullOrWhiteSpace(token?.AccessToken)) return null;

            // Refresh a little early.
            var ttl = TimeSpan.FromSeconds(Math.Max(60, token!.ExpiresIn - 300));
            await this.cache.SetStringAsync(
                TokenCacheKey,
                token.AccessToken!,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
                ct);

            return token.AccessToken;
        }

        // Accepts "name", "@name", "twitch.tv/name", "https://www.twitch.tv/name?x=1".
        private static string NormalizeLogin(string handle)
        {
            if (string.IsNullOrWhiteSpace(handle)) return string.Empty;

            var h = handle.Trim();
            var marker = "twitch.tv/";
            var idx = h.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) h = h.Substring(idx + marker.Length);

            h = h.TrimStart('@');
            var slash = h.IndexOf('/');
            if (slash >= 0) h = h.Substring(0, slash);
            var q = h.IndexOf('?');
            if (q >= 0) h = h.Substring(0, q);

            return h.Trim().ToLowerInvariant();
        }

        private sealed class TwitchTokenResponse
        {
            [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
            [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        }

        private sealed class TwitchUsersResponse
        {
            [JsonPropertyName("data")] public List<TwitchUser>? Data { get; set; }
        }

        private sealed class TwitchUser
        {
            [JsonPropertyName("id")] public string? Id { get; set; }
        }

        private sealed class TwitchVideosResponse
        {
            [JsonPropertyName("data")] public List<TwitchVideo>? Data { get; set; }
        }

        private sealed class TwitchVideo
        {
            [JsonPropertyName("id")] public string? Id { get; set; }
            [JsonPropertyName("url")] public string? Url { get; set; }
            [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
        }
    }
}
