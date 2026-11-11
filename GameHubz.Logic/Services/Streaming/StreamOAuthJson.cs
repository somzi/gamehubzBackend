using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace GameHubz.Logic.Services
{
    /// <summary>Token-endpoint plumbing shared by the Twitch and Kick account providers.</summary>
    internal static class StreamOAuthJson
    {
        /// <summary>
        /// Posts a form to an OAuth token endpoint and reads the standard response. Twitch returns the
        /// scopes as an array, Kick as a space-separated string; both are flattened to the string form.
        ///
        /// Null means the platform refused the grant (400 / 401: invalid_grant, revoked) — only then does
        /// the account need a re-link. A temporary failure (429, 5xx, timeout, no network) throws
        /// <see cref="StreamPlatformUnavailableException"/> instead, so it never costs the player the link.
        /// </summary>
        public static async Task<StreamOAuthTokens?> PostTokenAsync(
            HttpClient client, string url, Dictionary<string, string> form, ILogger logger, string platformName, CancellationToken ct)
        {
            using var response = await SendAsync(client, () => new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(form),
            }, platformName, ct);

            if (!response.IsSuccessStatusCode)
            {
                // The body names the reason (invalid_grant, invalid_client …) and never echoes a token back.
                string body = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("{Platform} token endpoint returned {Status}: {Body}",
                    platformName, (int)response.StatusCode, body.Length > 300 ? body[..300] : body);

                if (IsRefusal(response.StatusCode)) return null;
                throw new StreamPlatformUnavailableException($"{platformName} token endpoint returned {(int)response.StatusCode}.");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            JsonElement root = doc.RootElement;

            string? access = GetString(root, "access_token");
            string? refresh = GetString(root, "refresh_token");
            if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh)) return null;

            int expiresIn = root.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var seconds)
                ? seconds
                : 3600;

            string? scopes = null;
            if (root.TryGetProperty("scope", out var scope))
            {
                scopes = scope.ValueKind switch
                {
                    JsonValueKind.Array => string.Join(' ', scope.EnumerateArray().Select(s => s.GetString())),
                    JsonValueKind.String => scope.GetString(),
                    _ => null,
                };
            }

            return new StreamOAuthTokens(access, refresh, expiresIn, scopes);
        }

        /// <summary>
        /// A channel-owner API call (stream key, title, live check). 401 → <see cref="StreamTokenRejectedException"/>
        /// (revoked or expired early: refresh and retry); 429 / 5xx / no network →
        /// <see cref="StreamPlatformUnavailableException"/>. Any other answer is returned as it is.
        /// </summary>
        public static async Task<HttpResponseMessage> SendApiAsync(
            HttpClient client, Func<HttpRequestMessage> makeRequest, string platformName, CancellationToken ct)
        {
            var response = await SendAsync(client, makeRequest, platformName, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                throw new StreamTokenRejectedException($"{platformName} refused the access token.");
            }

            if (IsTemporary(response.StatusCode))
            {
                int status = (int)response.StatusCode;
                response.Dispose();
                throw new StreamPlatformUnavailableException($"{platformName} API returned {status}.");
            }

            return response;
        }

        private static async Task<HttpResponseMessage> SendAsync(
            HttpClient client, Func<HttpRequestMessage> makeRequest, string platformName, CancellationToken ct)
        {
            using var request = makeRequest();
            try
            {
                return await client.SendAsync(request, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new StreamPlatformUnavailableException($"{platformName} could not be reached.", ex);
            }
        }

        private static bool IsRefusal(System.Net.HttpStatusCode status) =>
            status == System.Net.HttpStatusCode.BadRequest || status == System.Net.HttpStatusCode.Unauthorized;

        private static bool IsTemporary(System.Net.HttpStatusCode status) =>
            status == System.Net.HttpStatusCode.TooManyRequests || (int)status >= 500;

        public static async Task<JsonDocument?> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
        {
            if (!response.IsSuccessStatusCode) return null;
            string body = await response.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(body) ? null : JsonDocument.Parse(body);
        }

        public static string? GetString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value)) return null;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            };
        }

        /// <summary>First element of the "data" array most Twitch / Kick responses wrap their payload in.</summary>
        public static JsonElement? FirstData(JsonDocument doc)
        {
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var item in data.EnumerateArray()) return item;
            return null;
        }
    }
}
