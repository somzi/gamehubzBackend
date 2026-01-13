using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GameHubz.Common.Consts;
using GameHubz.DataModels.Tokens;

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

            if (roleClaim == null)
            {
                throw new InvalidTokenException("Role is missing in token.");
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
                    Username = username?.Value ?? "",
                    Role = userRole.ToString(),
                    RoleEnum = userRole
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
                    };
            }
            else
            {
                throw new InvalidTokenException();
            }
        }
    }
}
