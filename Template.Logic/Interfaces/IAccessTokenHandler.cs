using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Template.Logic.Interfaces
{
    public interface IAccessTokenHandler
    {
        string WriteToken(JwtSecurityToken jwt);

        ClaimsPrincipal ValidateToken(string token, TokenValidationParameters tokenValidationParameters);

        ClaimsPrincipal GetPrincipalFromToken(string token, string signingKey);
    }
}