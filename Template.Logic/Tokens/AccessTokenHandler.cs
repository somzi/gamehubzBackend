using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Template.Logic.Tokens
{
    public sealed class AccessTokenHandler : IAccessTokenHandler
    {
        private readonly JwtSecurityTokenHandler jwtSecurityTokenHandler;

        public AccessTokenHandler()
        {
            this.jwtSecurityTokenHandler = new JwtSecurityTokenHandler();
        }

        public string WriteToken(JwtSecurityToken jwt)
        {
            return this.jwtSecurityTokenHandler.WriteToken(jwt);
        }

        public ClaimsPrincipal ValidateToken(string token, TokenValidationParameters tokenValidationParameters)
        {
            ClaimsPrincipal principal;
            SecurityToken securityToken;

            try
            {
                principal = this.jwtSecurityTokenHandler.ValidateToken(token, tokenValidationParameters, out securityToken);
            }
            catch (ArgumentNullException ex)
            {
                throw new InvalidTokenException(ex);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidTokenException(ex);
            }
            catch (SecurityTokenDecryptionFailedException ex)
            {
                throw new InvalidTokenException(ex);
            }
            catch (SecurityTokenException ex)
            {
                throw new InvalidTokenException(ex);
            }

            if (!(securityToken is JwtSecurityToken jwtSecurityToken)
                   || !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidTokenException();
            }

            return principal;
        }

        public ClaimsPrincipal GetPrincipalFromToken(string token, string signingKey)
        {
            return this.ValidateToken(token, new TokenValidationParameters
            {
                ValidateAudience = false,
                ValidateIssuer = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                ValidateLifetime = false, // we are checking expired tokens here
            });
        }
    }
}