using System.Security.Cryptography;
using System.Text;

namespace GameHubz.Logic.Utility
{
    /// <summary>
    /// The cryptography behind a result verification, kept in one small pure class so it can be tested
    /// on its own and so the phone and the server have exactly one definition of the signed message.
    ///
    /// The scheme is an HMAC over a server challenge. At registration the server issues a random key;
    /// the GameHubz app stores it in the Keychain / Keystore behind biometric access control, so on the
    /// phone the operating system only releases it after a successful Face ID / Touch ID / fingerprint
    /// unlock. A valid signature proves possession of the key issued for that installation, for this one
    /// attempt: the challenge is single-use and short-lived, and every id is inside the signed message.
    ///
    /// What a signature does NOT prove on its own is that a biometric check happened. The key reaches
    /// the client at registration, so anyone signed in to the account can register an "installation"
    /// from a script, keep the key, and sign challenges without ever touching Face ID — no modified app
    /// needed. Only platform attestation (iOS App Attest; Android hardware key attestation / Play
    /// Integrity, with the key generated in secure hardware rather than issued by us) lets the server
    /// verify that the key is locked behind biometrics. Until that layer exists the biometric step is
    /// enforced by the app, and the server vouches for account, installation, attempt and timestamps.
    /// </summary>
    public static class VerificationProof
    {
        /// <summary>
        /// Version tag inside the signed message. Bumping it invalidates every signature made under the
        /// old format instead of letting two formats be confused with each other.
        /// </summary>
        public const string MessagePrefix = "gamehubz.verify.v1";

        private const int SecretBytes = 32;
        private const int ChallengeBytes = 32;

        public static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(SecretBytes)).ToLowerInvariant();

        public static string NewChallenge() => Convert.ToHexString(RandomNumberGenerator.GetBytes(ChallengeBytes)).ToLowerInvariant();

        /// <summary>
        /// The exact string the phone signs. Every id of the attempt is in it, so a signature is only
        /// good for this verification, of this match, by this user, from this device — lifting it onto
        /// any other attempt fails. Lower-case "D" format GUIDs throughout, so there is one spelling.
        /// </summary>
        public static string BuildMessage(Guid verificationId, Guid matchId, Guid userId, Guid deviceId, string challenge)
            => string.Join('|',
                MessagePrefix,
                verificationId.ToString("D"),
                matchId.ToString("D"),
                userId.ToString("D"),
                deviceId.ToString("D"),
                challenge.ToLowerInvariant());

        public static string Sign(string secretHex, string message)
        {
            using var hmac = new HMACSHA256(Convert.FromHexString(secretHex));
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(message))).ToLowerInvariant();
        }

        /// <summary>
        /// Constant-time comparison against the expected signature. Anything that is not well-formed hex
        /// of the right length is simply a failed proof, never an exception.
        /// </summary>
        public static bool IsValid(string secretHex, string message, string? signatureHex)
        {
            if (string.IsNullOrWhiteSpace(signatureHex) || signatureHex.Length != 64) return false;

            byte[] provided;
            try
            {
                provided = Convert.FromHexString(signatureHex);
            }
            catch (FormatException)
            {
                return false;
            }

            byte[] expected = Convert.FromHexString(Sign(secretHex, message));
            return CryptographicOperations.FixedTimeEquals(expected, provided);
        }

        /// <summary>
        /// Hash for the platform's install id. Only ever compared for equality, so a plain SHA-256 with a
        /// fixed context string is enough to keep the raw id out of the database.
        /// </summary>
        public static string? HashPlatformDeviceId(string? platformDeviceId)
        {
            if (string.IsNullOrWhiteSpace(platformDeviceId)) return null;

            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes("gamehubz.device|" + platformDeviceId.Trim()));
            return Convert.ToHexString(digest).ToLowerInvariant();
        }
    }
}
