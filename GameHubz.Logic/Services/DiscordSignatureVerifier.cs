using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using System.Text;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Verifies the Ed25519 signature Discord attaches to every interactions request
    /// (X-Signature-Ed25519 / X-Signature-Timestamp over timestamp + raw body). Must run on the RAW
    /// request body, before any deserialization. Returns false on any malformed input — the caller
    /// answers 401, which is also how Discord probes the endpoint when the URL is saved.
    /// </summary>
    public static class DiscordSignatureVerifier
    {
        public static bool Verify(string publicKeyHex, string? signatureHex, string? timestamp, string rawBody)
        {
            if (string.IsNullOrWhiteSpace(publicKeyHex)
                || string.IsNullOrWhiteSpace(signatureHex)
                || string.IsNullOrWhiteSpace(timestamp))
                return false;

            try
            {
                var publicKey = new Ed25519PublicKeyParameters(Convert.FromHexString(publicKeyHex), 0);
                var signature = Convert.FromHexString(signatureHex);
                var message = Encoding.UTF8.GetBytes(timestamp + rawBody);

                var verifier = new Ed25519Signer();
                verifier.Init(forSigning: false, publicKey);
                verifier.BlockUpdate(message, 0, message.Length);
                return verifier.VerifySignature(signature);
            }
            catch
            {
                // Bad hex / wrong key length / anything unexpected → treat as an invalid signature.
                return false;
            }
        }
    }
}
