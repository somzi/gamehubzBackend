using System.Security.Cryptography;

namespace Template.Logic.Tokens
{
    public sealed class RefreshTokenFactory : IRefreshTokenFactory
    {
        public RefreshTokenFactory()
        {
        }

        public string GenerateToken(int size = 32)
        {
            var randomNumber = new byte[size];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }
    }
}