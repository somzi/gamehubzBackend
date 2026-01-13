using System.Text;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.Extensions.Configuration;

namespace Template.Logic.Crypto
{
    public class Pbkdf2Hasher : IPasswordHasher
    {
        private readonly string papper;

        public Pbkdf2Hasher(IConfiguration configuration)
        {
            this.papper = configuration.GetStringThrowIfNull("AuthSettings:PasswordKey");
        }

        public string HashPassword(string password, string passwordSalt)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentException($"'{nameof(password)}' cannot be null or whitespace.", nameof(password));
            }

            if (string.IsNullOrWhiteSpace(passwordSalt))
            {
                throw new ArgumentException($"'{nameof(passwordSalt)}' cannot be null or whitespace.", nameof(passwordSalt));
            }

            password += this.papper;

            string hashed = Convert.ToBase64String(KeyDerivation.Pbkdf2(
                password: password!,
                salt: Encoding.UTF8.GetBytes(passwordSalt),
                prf: KeyDerivationPrf.HMACSHA512,
                iterationCount: 10000,
                numBytesRequested: 32));

            return hashed;
        }
    }
}