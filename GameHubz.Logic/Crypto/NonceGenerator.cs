using System.Security.Cryptography;

namespace GameHubz.Logic.Crypto
{
    public static class NonceGenerator
    {
        public const string Characters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890!@#$%^&*()_-+=?:;|{}[]<>,.~";

        /// <summary>
        /// Generates a per-user password salt.
        /// </summary>
        /// <remarks>
        /// This used to draw from <c>Random</c> seeded with (sum of a GUID's characters ×
        /// <c>DateTime.Now.Millisecond</c>). That seed carries roughly 17 bits of entropy instead of
        /// the ~103 that 16 characters of this alphabet should hold, so salts started colliding
        /// across accounts after a few hundred users — and the seed collapsed to a flat 0 whenever
        /// Millisecond happened to be 0, handing every account created in that instant the same
        /// salt. Colliding salts mean two users with the same password get the same hash, which is
        /// exactly what a per-user salt exists to prevent. RandomNumberGenerator removes the seed.
        ///
        /// Existing accounts are unaffected: each one keeps the PasswordNonce already stored on its
        /// row, and verification always re-reads that stored value. This only changes salts minted
        /// from here on, so no stored hash is invalidated.
        /// </remarks>
        public static string GetNew(int length = 16)
        {
            char[] nonce = new char[length];

            for (int i = 0; i < length; i++)
            {
                // GetInt32 is rejection-sampled, so the distribution stays uniform. The old loop
                // used Next(0, Characters.Length - 1), which could never pick the final character.
                nonce[i] = Characters[RandomNumberGenerator.GetInt32(Characters.Length)];
            }

            return new string(nonce);
        }
    }
}
