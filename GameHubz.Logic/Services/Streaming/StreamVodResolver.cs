using GameHubz.DataModels.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GameHubz.Logic.Services
{
    // Picks the right platform client and resolves a VOD url defensively: any failure returns null
    // so the end-stream flow always succeeds and the UI can offer manual entry as a fallback.
    public class StreamVodResolver
    {
        private readonly IEnumerable<IStreamPlatformClient> clients;
        private readonly IConfiguration config;
        private readonly ILogger<StreamVodResolver> logger;

        public StreamVodResolver(
            IEnumerable<IStreamPlatformClient> clients,
            IConfiguration config,
            ILogger<StreamVodResolver> logger)
        {
            this.clients = clients;
            this.config = config;
            this.logger = logger;
        }

        public async Task<string?> ResolveVodUrlAsync(
            SocialType platform,
            string handle,
            DateTime startedAtUtc,
            DateTime endedAtUtc,
            CancellationToken cancellationToken = default)
        {
            string? resolved = null;

            var client = this.clients.FirstOrDefault(c => c.Platform == platform);
            if (client != null)
            {
                try
                {
                    resolved = await client.TryResolveVodUrlAsync(handle, startedAtUtc, endedAtUtc, cancellationToken);
                }
                catch (Exception ex)
                {
                    this.logger.LogWarning(ex, "VOD resolution failed for {Platform} handle '{Handle}'", platform, handle);
                }
            }
            else
            {
                this.logger.LogWarning("No stream platform client registered for {Platform}", platform);
            }

            // Development-only fallback: lets the replay flow be tested end-to-end without real
            // platform API keys. Set Streaming:DevFakeVodUrl in appsettings.Development only;
            // leave it empty in production (real keys drive resolution there).
            //
            // The fake is applied ONLY when its url platform matches the stream's platform —
            // otherwise a Kick stream would render as YouTube (the embed builder picks the
            // platform off the url), which is misleading during dev testing.
            if (string.IsNullOrWhiteSpace(resolved))
            {
                var devFake = this.config["Streaming:DevFakeVodUrl"];
                if (!string.IsNullOrWhiteSpace(devFake) && DetectPlatform(devFake) == platform)
                {
                    this.logger.LogInformation("Streaming:DevFakeVodUrl fallback used for {Platform}.", platform);
                    resolved = devFake;
                }
            }

            return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
        }

        private static SocialType? DetectPlatform(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var u = url.ToLowerInvariant();
            if (u.Contains("youtube.com") || u.Contains("youtu.be")) return SocialType.YouTube;
            if (u.Contains("twitch.tv")) return SocialType.Twitch;
            if (u.Contains("kick.com")) return SocialType.Kick;
            return null;
        }
    }
}
