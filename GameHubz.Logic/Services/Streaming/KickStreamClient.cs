using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameHubz.DataModels.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GameHubz.Logic.Services
{
    // Resolves a Kick VOD link for the channel slug stored on the MatchStream row.
    //
    // WHY THIS SHELLS OUT TO curl INSTEAD OF HttpClient:
    //   Kick exposes past videos only at the web endpoint kick.com/api/v2/channels/{slug}/videos
    //   (the official api.kick.com/public/v1 API has no videos endpoint — every variant 404s).
    //   That web endpoint sits behind Cloudflare bot management which fingerprints the TLS
    //   ClientHello (JA3/JA4). .NET's SslStream produces a fingerprint Cloudflare rejects with
    //   403 — verified across HTTP/1.1, HTTP/2, custom SocketsHttpHandler, full browser headers:
    //   every managed-HttpClient variant returned 403. A real browser TLS fingerprint returns 200.
    //
    //   So we delegate the request to an external curl that presents a browser-like fingerprint:
    //     • Production (Linux/Docker): curl-impersonate's curl_chrome* wrapper (real Chrome JA3+HTTP2).
    //     • Dev (Windows): the system curl.exe (Schannel) with browser headers, which also passes.
    //   Both targets were validated end-to-end against the live endpoint.
    //
    // The command is configurable (Streaming:Kick:CurlCommand / :CurlImpersonate); any failure
    // (missing curl, non-zero exit, empty/blocked body, bad JSON) returns null → manual fallback.
    public class KickStreamClient : IStreamPlatformClient
    {
        public SocialType Platform => SocialType.Kick;

        private const string BrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";

        // Hard cap on the curl call. Kept under MatchService.EndStream's 10s cancellation budget so
        // curl self-terminates cleanly (and we still get to parse) rather than being killed mid-flight.
        private const int CurlMaxSeconds = 8;

        private readonly IConfiguration config;
        private readonly ILogger<KickStreamClient> logger;

        public KickStreamClient(IConfiguration config, ILogger<KickStreamClient> logger)
        {
            this.config = config;
            this.logger = logger;
        }

        public async Task<string?> TryResolveVodUrlAsync(
            string handle,
            DateTime startedAtUtc,
            DateTime endedAtUtc,
            CancellationToken cancellationToken = default)
        {
            var slug = NormalizeSlug(handle);
            if (string.IsNullOrWhiteSpace(slug)) return null;

            var url = $"https://kick.com/api/v2/channels/{Uri.EscapeDataString(slug)}/videos";

            var json = await FetchViaCurlAsync(url, cancellationToken);
            if (string.IsNullOrWhiteSpace(json)) return null;

            List<KickVideoItem>? videos;
            try
            {
                videos = JsonSerializer.Deserialize<List<KickVideoItem>>(json);
            }
            catch (JsonException ex)
            {
                // A 403 challenge page (or any non-array body) lands here — treat as "no VOD".
                this.logger.LogDebug(ex, "Kick videos payload for '{Slug}' was not a video array.", slug);
                return null;
            }

            if (videos == null || videos.Count == 0) return null;

            // Prefer a video whose start_time sits inside the just-finished stream window
            // (with a 2h lead-in for clock skew / pre-roll). Otherwise take the newest.
            var windowStart = startedAtUtc.AddHours(-2);
            var best = videos
                .Where(v => v.StartTimeUtc == null || v.StartTimeUtc >= windowStart)
                .OrderByDescending(v => v.StartTimeUtc ?? v.CreatedAtUtc ?? DateTime.MinValue)
                .FirstOrDefault()
                ?? videos
                    .OrderByDescending(v => v.StartTimeUtc ?? v.CreatedAtUtc ?? DateTime.MinValue)
                    .FirstOrDefault();

            if (best == null) return null;

            // Prefer the direct HLS manifest ("source") — it's a CORS-open master.m3u8 the app plays
            // in-app via hls.js. Kick's own player page (player.kick.com/video/{uuid}) needs internal
            // config and renders "misconfigured" when embedded, and kick.com/video/{uuid} 404s — so
            // we never return those. If the manifest is missing, fall back to the real watch page
            // (kick.com/{slug}/videos/{uuid}) which at least opens correctly in a browser.
            if (!string.IsNullOrWhiteSpace(best.Source))
                return best.Source;

            var uuid = best.Video?.Uuid;
            return string.IsNullOrWhiteSpace(uuid)
                ? null
                : $"https://kick.com/{slug}/videos/{uuid}";
        }

        // Runs the configured curl and returns stdout, or null on any failure.
        private async Task<string?> FetchViaCurlAsync(string url, CancellationToken cancellationToken)
        {
            var command = this.config["Streaming:Kick:CurlCommand"];
            if (string.IsNullOrWhiteSpace(command)) command = "curl";

            // curl-impersonate wrappers (curl_chrome*) set their own browser TLS + headers, so we
            // pass only the essentials. Plain curl needs the headers and an HTTP/1.1 downgrade.
            var impersonate = bool.TryParse(this.config["Streaming:Kick:CurlImpersonate"], out var imp) && imp;

            var psi = new ProcessStartInfo
            {
                FileName = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            psi.ArgumentList.Add("-s");                      // silent (no progress meter)
            psi.ArgumentList.Add("--max-time");
            psi.ArgumentList.Add(CurlMaxSeconds.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-H");
            psi.ArgumentList.Add("Accept: application/json, text/plain, */*");
            psi.ArgumentList.Add("-H");
            psi.ArgumentList.Add("Referer: https://kick.com/");

            if (!impersonate)
            {
                psi.ArgumentList.Add("--http1.1");
                psi.ArgumentList.Add("-H");
                psi.ArgumentList.Add("User-Agent: " + BrowserUserAgent);
            }

            psi.ArgumentList.Add(url);                       // URL last; ArgumentList ⇒ no shell injection

            Process? process = null;
            try
            {
                process = Process.Start(psi);
                if (process == null)
                {
                    this.logger.LogWarning("Kick: failed to start curl command '{Command}'.", command);
                    return null;
                }

                // Drain BOTH pipes concurrently — if curl writes enough to stderr and we never read
                // it, the pipe buffer fills and curl blocks, hanging until the cancellation kicks in.
                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    TryKill(process);
                    return null;
                }

                var output = await stdoutTask;
                var error = await stderrTask;

                if (process.ExitCode != 0)
                {
                    this.logger.LogWarning(
                        "Kick: curl '{Command}' exited {Code} for {Url}. stderr: {Err}",
                        command, process.ExitCode, url, Trim(error));
                    return null;
                }

                if (string.IsNullOrWhiteSpace(output))
                {
                    this.logger.LogWarning(
                        "Kick: curl '{Command}' returned empty body (exit 0) for {Url}. stderr: {Err}",
                        command, url, Trim(error));
                    return null;
                }

                return output;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // curl binary not found / not executable — log loudly: VOD auto-resolution won't work.
                // On Windows prefer the absolute path C:\Windows\System32\curl.exe in config to avoid
                // PATH ambiguity (a stale curl earlier on PATH is a common cause).
                this.logger.LogWarning(ex, "Kick: curl command '{Command}' could not be executed.", command);
                return null;
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, "Kick: curl invocation failed for {Url}.", url);
                return null;
            }
            finally
            {
                process?.Dispose();
            }
        }

        // Keeps a stderr snippet short for logs.
        private static string Trim(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "(none)";
            s = s.Trim();
            return s.Length > 300 ? s.Substring(0, 300) + "…" : s;
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // best-effort
            }
        }

        // Accepts "slug", "@slug", "kick.com/slug", "https://kick.com/slug?x=1".
        private static string NormalizeSlug(string handle)
        {
            if (string.IsNullOrWhiteSpace(handle)) return string.Empty;

            var h = handle.Trim();
            var marker = "kick.com/";
            var idx = h.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) h = h.Substring(idx + marker.Length);

            h = h.TrimStart('@');
            var slash = h.IndexOf('/');
            if (slash >= 0) h = h.Substring(0, slash);
            var q = h.IndexOf('?');
            if (q >= 0) h = h.Substring(0, q);

            return h.Trim().ToLowerInvariant();
        }

        private static DateTime? ParseKickUtc(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return DateTime.TryParseExact(
                raw,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dt)
                ? dt
                : null;
        }

        // Shape of an item from kick.com/api/v2/channels/{slug}/videos.
        // NB: Kick serializes dates as "yyyy-MM-dd HH:mm:ss" (space, no T), which System.Text.Json
        // refuses to bind to DateTime directly — so the raw strings are captured and parsed lazily.
        private sealed class KickVideoItem
        {
            [JsonPropertyName("id")] public long Id { get; set; }
            [JsonPropertyName("slug")] public string? Slug { get; set; }
            [JsonPropertyName("created_at")] public string? CreatedAtRaw { get; set; }
            [JsonPropertyName("start_time")] public string? StartTimeRaw { get; set; }
            // Direct HLS master playlist (master.m3u8) for this VOD — CORS-open, plays via hls.js.
            [JsonPropertyName("source")] public string? Source { get; set; }
            [JsonPropertyName("video")] public KickVideoInner? Video { get; set; }

            [JsonIgnore] public DateTime? CreatedAtUtc => ParseKickUtc(this.CreatedAtRaw);
            [JsonIgnore] public DateTime? StartTimeUtc => ParseKickUtc(this.StartTimeRaw);
        }

        private sealed class KickVideoInner
        {
            [JsonPropertyName("uuid")] public string? Uuid { get; set; }
        }
    }
}
