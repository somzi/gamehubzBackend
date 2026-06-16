using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GameHubz.DataModels.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GameHubz.Logic.Services
{
    // YouTube Data API v3 (API key, no OAuth). A finished live stream becomes a regular video at the
    // SAME id, so the replay link is permanent. Resolution is one-shot on stream end.
    // Quota note: channel-id-by-search costs 100 units; everything else is cheap. We resolve once
    // per ended stream, so default quota (10k/day) is ample.
    public class YouTubeStreamClient : IStreamPlatformClient
    {
        public SocialType Platform => SocialType.YouTube;

        private readonly IHttpClientFactory httpClientFactory;
        private readonly IConfiguration config;
        private readonly ILogger<YouTubeStreamClient> logger;

        public YouTubeStreamClient(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<YouTubeStreamClient> logger)
        {
            this.httpClientFactory = httpClientFactory;
            this.config = config;
            this.logger = logger;
        }

        // Standalone YouTube channel-id resolver. Called from MatchService at stream start to convert
        // an @handle to the stable "UC..." channel id so the YouTube LIVE embed works in-app
        // (live_stream?channel=UC.. requires a channel id; @handles can't be embedded directly).
        // Returns null when no key is configured or the lookup fails — the caller keeps the
        // original handle in that case (it still works for Twitch/Kick / VOD playback).
        public async Task<string?> TryResolveChannelIdAsync(string handle, CancellationToken cancellationToken = default)
        {
            var apiKey = this.config["Streaming:YouTube:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                this.logger.LogDebug("YouTube API key missing — skipping channel-id resolution.");
                return null;
            }

            try
            {
                var client = this.httpClientFactory.CreateClient();
                return await ResolveChannelIdAsync(client, apiKey, handle, cancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, "YouTube channel-id resolution threw for '{Handle}'", handle);
                return null;
            }
        }

        public async Task<string?> TryResolveVodUrlAsync(
            string handle,
            DateTime startedAtUtc,
            DateTime endedAtUtc,
            CancellationToken cancellationToken = default)
        {
            var apiKey = this.config["Streaming:YouTube:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                this.logger.LogWarning("YouTube API key is not configured (Streaming:YouTube:ApiKey).");
                return null;
            }

            // If the streamer pasted the actual video/live link, the id IS the VOD.
            var directVideoId = ExtractVideoId(handle);
            if (!string.IsNullOrWhiteSpace(directVideoId))
                return WatchUrl(directVideoId!);

            var client = this.httpClientFactory.CreateClient();

            var channelId = await ResolveChannelIdAsync(client, apiKey, handle, cancellationToken);
            if (string.IsNullOrWhiteSpace(channelId)) return null;

            // A just-ended live broadcast surfaces as a "completed" video for the channel.
            var search = await TryGetAsync<YouTubeSearchResponse>(
                client,
                "https://www.googleapis.com/youtube/v3/search" +
                $"?part=snippet&channelId={Uri.EscapeDataString(channelId!)}" +
                "&eventType=completed&type=video&order=date&maxResults=5" +
                $"&key={apiKey}",
                cancellationToken);

            var videoId = search?.Items?
                .Select(i => i.Id?.VideoId)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            return string.IsNullOrWhiteSpace(videoId) ? null : WatchUrl(videoId!);
        }

        private async Task<string?> ResolveChannelIdAsync(
            HttpClient client, string apiKey, string handle, CancellationToken ct)
        {
            var clean = NormalizeHandle(handle);
            if (string.IsNullOrWhiteSpace(clean)) return null;

            // Already a channel id (UC...)
            if (clean.StartsWith("UC", StringComparison.Ordinal) && clean.Length >= 20)
                return clean;

            // Modern @handles resolve cheaply via forHandle (1 unit)
            var byHandle = await TryGetAsync<YouTubeChannelsResponse>(
                client,
                $"https://www.googleapis.com/youtube/v3/channels?part=id&forHandle={Uri.EscapeDataString(clean)}&key={apiKey}",
                ct);
            var id = byHandle?.Items?.FirstOrDefault()?.Id;
            if (!string.IsNullOrWhiteSpace(id)) return id;

            // Fallback: channel search (100 units)
            var search = await TryGetAsync<YouTubeSearchResponse>(
                client,
                $"https://www.googleapis.com/youtube/v3/search?part=snippet&type=channel&q={Uri.EscapeDataString(clean)}&maxResults=1&key={apiKey}",
                ct);

            return search?.Items?.FirstOrDefault()?.Id?.ChannelId
                ?? search?.Items?.FirstOrDefault()?.Snippet?.ChannelId;
        }

        private async Task<T?> TryGetAsync<T>(HttpClient client, string url, CancellationToken ct)
            where T : class
        {
            try
            {
                using var resp = await client.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) return null;
                return await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
            }
            catch
            {
                return null;
            }
        }

        private static string WatchUrl(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

        private static string? ExtractVideoId(string handle)
        {
            if (string.IsNullOrWhiteSpace(handle)) return null;
            var h = handle.Trim();

            var m = Regex.Match(h, @"[?&]v=([A-Za-z0-9_-]{11})");
            if (m.Success) return m.Groups[1].Value;

            m = Regex.Match(h, @"(?:youtu\.be/|/live/|/embed/|/shorts/)([A-Za-z0-9_-]{11})");
            if (m.Success) return m.Groups[1].Value;

            return null;
        }

        // Accepts "@handle", "handle", "youtube.com/@handle", "youtube.com/channel/UC..",
        // "youtube.com/c/Name". Returns the bare handle/channel-id token.
        private static string NormalizeHandle(string handle)
        {
            if (string.IsNullOrWhiteSpace(handle)) return string.Empty;
            var h = handle.Trim();

            if (h.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                h.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
            {
                var lastSegment = h.TrimEnd('/')
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .LastOrDefault() ?? h;
                h = lastSegment;
            }

            h = h.TrimStart('@');
            var q = h.IndexOf('?');
            if (q >= 0) h = h.Substring(0, q);

            return h.Trim();
        }

        private sealed class YouTubeSearchResponse
        {
            [JsonPropertyName("items")] public List<YouTubeSearchItem>? Items { get; set; }
        }

        private sealed class YouTubeSearchItem
        {
            [JsonPropertyName("id")] public YouTubeSearchId? Id { get; set; }
            [JsonPropertyName("snippet")] public YouTubeSnippet? Snippet { get; set; }
        }

        private sealed class YouTubeSearchId
        {
            [JsonPropertyName("videoId")] public string? VideoId { get; set; }
            [JsonPropertyName("channelId")] public string? ChannelId { get; set; }
        }

        private sealed class YouTubeSnippet
        {
            [JsonPropertyName("channelId")] public string? ChannelId { get; set; }
        }

        private sealed class YouTubeChannelsResponse
        {
            [JsonPropertyName("items")] public List<YouTubeChannelItem>? Items { get; set; }
        }

        private sealed class YouTubeChannelItem
        {
            [JsonPropertyName("id")] public string? Id { get; set; }
        }
    }
}
