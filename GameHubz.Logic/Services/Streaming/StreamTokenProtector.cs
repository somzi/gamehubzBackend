using System.Security.Cryptography;
using System.Text;
using GameHubz.DataModels.Config;
using Microsoft.Extensions.Options;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Encrypts the Twitch / Kick OAuth tokens kept on UserStreamAccount. AES-256-GCM with a fresh nonce
    /// per value, so equal tokens never produce equal ciphertexts and a tampered value fails to decrypt
    /// instead of decrypting to garbage. Stored as base64(nonce | tag | ciphertext).
    ///
    /// Also hashes the broadcast session tokens: those are only ever compared, never read back.
    /// </summary>
    public class StreamTokenProtector
    {
        private const int NonceSize = 12;
        private const int TagSize = 16;

        private readonly byte[]? key;

        public StreamTokenProtector(IOptions<StreamingConfig> options)
        {
            string raw = options.Value.TokenEncryptionKey;
            if (string.IsNullOrWhiteSpace(raw)) return;

            try
            {
                byte[] decoded = Convert.FromBase64String(raw.Trim());
                if (decoded.Length == 32) this.key = decoded;
            }
            catch (FormatException)
            {
                // Left null: IsConfigured reports false and linking stays off.
            }
        }

        /// <summary>False until Streaming:TokenEncryptionKey holds a base64 32-byte key.</summary>
        public bool IsConfigured => this.key != null;

        public string Protect(string plain)
        {
            byte[] k = this.key ?? throw new InvalidOperationException("Streaming:TokenEncryptionKey is not configured.");

            byte[] plainBytes = Encoding.UTF8.GetBytes(plain);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] cipher = new byte[plainBytes.Length];
            byte[] tag = new byte[TagSize];

            using (var aes = new AesGcm(k, TagSize))
            {
                aes.Encrypt(nonce, plainBytes, cipher, tag);
            }

            byte[] output = new byte[NonceSize + TagSize + cipher.Length];
            Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
            Buffer.BlockCopy(tag, 0, output, NonceSize, TagSize);
            Buffer.BlockCopy(cipher, 0, output, NonceSize + TagSize, cipher.Length);
            return Convert.ToBase64String(output);
        }

        /// <summary>Null when the value cannot be decrypted (wrong key, tampered, empty).</summary>
        public string? Unprotect(string? protectedValue)
        {
            if (this.key == null || string.IsNullOrEmpty(protectedValue)) return null;

            try
            {
                byte[] input = Convert.FromBase64String(protectedValue);
                if (input.Length < NonceSize + TagSize) return null;

                byte[] nonce = input.AsSpan(0, NonceSize).ToArray();
                byte[] tag = input.AsSpan(NonceSize, TagSize).ToArray();
                byte[] cipher = input.AsSpan(NonceSize + TagSize).ToArray();
                byte[] plain = new byte[cipher.Length];

                using var aes = new AesGcm(this.key, TagSize);
                aes.Decrypt(nonce, cipher, tag, plain);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                return null;
            }
        }

        /// <summary>A new URL-safe random token (256 bits).</summary>
        public static string NewToken()
        {
            return Base64Url(RandomNumberGenerator.GetBytes(32));
        }

        /// <summary>SHA-256 hex of a token — what gets stored and compared.</summary>
        public static string Hash(string token)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        }

        /// <summary>PKCE: the S256 code challenge for a verifier.</summary>
        public static string CodeChallenge(string verifier)
        {
            return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        }

        private static string Base64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
