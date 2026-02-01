using GameHubz.Common.Consts;
using GameHubz.DataModels.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace GameHubz.Logic.Tokens
{
    public class AccessTokenReader
    {
        private readonly ILocalizationService localizationService;
        private readonly IAppUnitOfWork AppUnitOfWork;

        public AccessTokenReader(ILocalizationService localizationService, IUnitOfWorkFactory factory)
        {
            this.localizationService = localizationService;
            this.AppUnitOfWork = factory.CreateAppUnitOfWork();
        }

        public async Task<TokenUserInfo> ReadFromClaimsPrincipal(ClaimsPrincipal claimsPrincipal)
        {
            if (claimsPrincipal is null)
            {
                throw new ParameterNullException(this.localizationService, nameof(claimsPrincipal));
            }

            Claim? idFromToken = claimsPrincipal.FindFirst(JwtClaimIdentifiers.Id);
            Claim? username = claimsPrincipal.FindFirst(JwtRegisteredClaimNames.Sub);
            Claim? emailFromToken = claimsPrincipal.FindFirst(ClaimTypes.Email);
            Claim? roleClaim = claimsPrincipal.FindFirst(ClaimTypes.Role);
            Claim? regionClaim = claimsPrincipal.FindFirst("region");
            Claim? userNickNameClaim = claimsPrincipal.FindFirst("userNickName");

            if (roleClaim == null)
            {
                throw new InvalidTokenException("Role is missing in token.");
            }

            int? region = null;

            if (regionClaim != null)
            {
                if (!int.TryParse(regionClaim.Value, out var regionInt))
                {
                    throw new InvalidTokenException("Region cannot be parsed.");
                }

                region = regionInt;
            }

            string userNickname = string.Empty;

            if (userNickNameClaim != null)
            {
                userNickname = userNickNameClaim.Value;
            }

            if (Enum.TryParse(roleClaim.Value, true, out UserRoleEnum userRole) == false)
            {
                throw new InvalidTokenException("Role cannot be parsed.");
            }

            if (idFromToken != null)
            {
                if (!Guid.TryParse(idFromToken.Value, out Guid userId))
                {
                    throw new InvalidTokenException();
                }

                return new TokenUserInfo()
                {
                    UserId = userId,
                    Username = userNickname,
                    Role = userRole.ToString(),
                    RoleEnum = userRole,
                    Region = region
                };
            }
            else if (emailFromToken != null)
            {
                var externalUser = await this.AppUnitOfWork.UserRepository.GetIdByEmail(emailFromToken.Value);

                return externalUser == null
                    ? throw new InvalidTokenException()
                    : new TokenUserInfo()
                    {
                        UserId = externalUser.Id!.Value,
                        Username = emailFromToken.Value,
                        Role = userRole.ToString(),
                        RoleEnum = userRole,
                        Region = region
                    };
            }
            else
            {
                throw new InvalidTokenException();
            }
        }
    }
}