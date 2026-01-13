using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Principal;
using Template.DataModels.Config;
using Template.DataModels.Tokens;
using Microsoft.Extensions.Options;

namespace Template.Logic.Tokens
{
    public sealed class AccessTokenFactory : IAccessTokenFactory
    {
        private readonly IAccessTokenHandler jwtTokenHandler;
        private readonly ILocalizationService localizationService;
        private readonly AccessTokenOptions jwtOptions;

        public AccessTokenFactory(
            IAccessTokenHandler jwtTokenHandler,
            IOptions<AccessTokenOptions> jwtOptions,
            ILocalizationService localizationService)
        {
            this.localizationService = localizationService;

            if (jwtOptions is null)
            {
                throw new ParameterNullException(this.localizationService, nameof(jwtOptions));
            }

            this.jwtTokenHandler = jwtTokenHandler;

            this.jwtOptions = jwtOptions.Value;

            this.ThrowIfInvalidOptions(this.jwtOptions);
        }

        public async Task<AccessToken> GenerateEncodedToken(TokenUserInfo tokenUserInfo)
        {
            if (tokenUserInfo is null)
            {
                throw new ParameterNullException(this.localizationService, nameof(tokenUserInfo));
            }

            var identity = GenerateClaimsIdentity(tokenUserInfo);

            var claims = new[]
            {
                 new Claim(JwtRegisteredClaimNames.Sub, tokenUserInfo.Username),
                 new Claim(JwtRegisteredClaimNames.Jti, await this.jwtOptions.JtiGenerator()),
                 new Claim(JwtRegisteredClaimNames.Iat, ToUnixEpochDate(this.jwtOptions.IssuedAt).ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
                 identity.FindFirst(JwtClaimIdentifiers.Rol),
                 identity.FindFirst(JwtClaimIdentifiers.Id),
                 identity.FindFirst(ClaimTypes.Role),
            };

            // Create the JWT security token and encode it.
            var jwt = new JwtSecurityToken(
                this.jwtOptions.Issuer,
                this.jwtOptions.Audience,
                claims,
                this.jwtOptions.NotBefore,
                this.jwtOptions.Expiration,
                this.jwtOptions.SigningCredentials);

            return new AccessToken(this.jwtTokenHandler.WriteToken(jwt), (int)this.jwtOptions.ValidFor.TotalSeconds);
        }

        private static ClaimsIdentity GenerateClaimsIdentity(TokenUserInfo tokenUserInfo)
        {
            return new(new GenericIdentity(tokenUserInfo.Username, "Token"),
                        [
                            new Claim(JwtClaimIdentifiers.Id, tokenUserInfo.UserId.ToString()),
                            new Claim(JwtClaimIdentifiers.Rol, JwtClaims.ApiAccess),
                            new Claim(ClaimTypes.Role, tokenUserInfo.Role),
                        ]);
        }

        /// <returns>Date converted to seconds since Unix epoch (Jan 1, 1970, midnight UTC).</returns>
        private static long ToUnixEpochDate(DateTime date)
              => (long)Math.Round((date.ToUniversalTime() -
                    new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero))
                    .TotalSeconds);

        private void ThrowIfInvalidOptions(AccessTokenOptions options)
        {
            if (options == null)
            {
                throw new ParameterNullException(this.localizationService, nameof(options));
            }

            if (options.ValidFor <= TimeSpan.Zero)
            {
                throw new InvalidTokenConfigurationException(Strings.InvalidTokenConfigurationException_ValidFor, nameof(AccessTokenOptions.ValidFor));
            }

            if (options.SigningCredentials == null)
            {
                throw new InvalidTokenConfigurationException(nameof(AccessTokenOptions.SigningCredentials));
            }

            if (options.JtiGenerator == null)
            {
                throw new InvalidTokenConfigurationException(nameof(AccessTokenOptions.JtiGenerator));
            }
        }
    }
}