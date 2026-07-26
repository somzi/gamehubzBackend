using System.Text.RegularExpressions;

namespace GameHubz.Logic.Utility
{
    /// <summary>
    /// Strips credentials out of a request body before it is written anywhere durable.
    /// </summary>
    /// <remarks>
    /// Error logging captures the request body so a fault can be reproduced, but on the auth
    /// surface that body is the credential itself: a 500 during login persisted the user's
    /// plaintext password, and a wrong-old-password 400 persisted both the old and the new one.
    /// Tokens get the same treatment as passwords — a leaked refresh token is account access for
    /// as long as it lives, so it is no less sensitive than the password that minted it.
    /// </remarks>
    public static class SensitiveDataRedactor
    {
        public const string Placeholder = "***";

        // Substring match on the property name, so oldPassword / confirmPassword / forgotPasswordOtp
        // and anything else in that shape are covered without having to enumerate every DTO field.
        // Over-redacting an innocent field costs a little debugging context; missing one writes a
        // credential to disk, so the trade is deliberately lopsided.
        private const string SensitiveKeyFragments = "password|token|secret|otp|apikey|credential";

        // Matches "<sensitive key>": <scalar>, where the scalar is a JSON string (escapes handled),
        // a number, a bool or null. Objects/arrays are left alone — no DTO nests a credential.
        private static readonly Regex SensitiveJsonValue = new(
            $@"""(?<key>[^""\\]*(?:{SensitiveKeyFragments})[^""\\]*)""\s*:\s*(?<value>""(?:\\.|[^""\\])*""|-?\d+(?:\.\d+)?|true|false|null)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(200));

        /// <summary>
        /// Returns <paramref name="body"/> with any credential-shaped values replaced.
        /// </summary>
        /// <param name="path">Request path, used to recognise the auth surface.</param>
        /// <param name="body">Raw request body.</param>
        public static string Redact(string? path, string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return string.Empty;
            }

            // Some auth routes bind a bare JSON scalar rather than an object — POST /Auth/logout is
            // the refresh token as a quoted string, with no property name to match on. There is
            // nothing to key off there, so anything on that surface that isn't a JSON object is
            // dropped whole instead of guessed at.
            if (IsAuthSurface(path) && !LooksLikeJsonObject(body))
            {
                return Placeholder;
            }

            try
            {
                return SensitiveJsonValue.Replace(body, m => $"\"{m.Groups["key"].Value}\":\"{Placeholder}\"");
            }
            catch (RegexMatchTimeoutException)
            {
                // The body is attacker-controlled, so a pathological input must never fall through
                // to "store it raw" — drop it instead.
                return Placeholder;
            }
        }

        private static bool IsAuthSurface(string? path)
            => path?.Contains("/auth", StringComparison.OrdinalIgnoreCase) == true;

        private static bool LooksLikeJsonObject(string body)
            => body.AsSpan().TrimStart().StartsWith("{");
    }
}
