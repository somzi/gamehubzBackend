using Google.Apis.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace GameHubz.Api.AuthenticationSchemes
{
    public class GoogleAuthenticationScheme : ISecurityTokenValidator
    {
        private readonly string clientId;
        private readonly JwtSecurityTokenHandler tokenHandler;

        public GoogleAuthenticationScheme(string clientId)
        {
            this.clientId = clientId;
            this.tokenHandler = new JwtSecurityTokenHandler();
        }

        public bool CanValidateToken => true;

        public int MaximumTokenSizeInBytes { get; set; } = TokenValidationParameters.DefaultMaximumTokenSizeInBytes;

        public bool CanReadToken(string securityToken)
        {
            return tokenHandler.CanReadToken(securityToken);
        }

        public ClaimsPrincipal ValidateToken(string securityToken, TokenValidationParameters validationParameters, out SecurityToken validatedToken)
        {
            validatedToken = tokenHandler.ReadJwtToken(securityToken);
            var principle = new ClaimsPrincipal();

            GoogleJsonWebSignature.Payload payload
                = Task.Run(async () => await GoogleJsonWebSignature
                    .ValidateAsync(
                        securityToken,
                        new GoogleJsonWebSignature.ValidationSettings()
                        {
                            Audience = new[] { clientId }
                        })).Result;

            var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, payload.GivenName),
                        new Claim(ClaimTypes.Surname, payload.FamilyName),
                        new Claim(ClaimTypes.Email, payload.Email),
                    };

            principle.AddIdentity(new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme));
            return principle;
        }
    }
}
