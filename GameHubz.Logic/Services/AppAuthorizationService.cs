using GameHubz.Common.Consts;

namespace GameHubz.Logic.Services
{
    public class AppAuthorizationService
    {
        private readonly ILocalizationService localizationService;
        private readonly IUserContextReader userContextReader;

        public AppAuthorizationService(
            ILocalizationService localizationService,
            IUserContextReader userContextReader)
        {
            this.localizationService = localizationService;
            this.userContextReader = userContextReader;
        }

        public async Task CheckAuthorization(
            UserRoleEnum[]? allowedRoles = null)
        {
            if (allowedRoles == null || allowedRoles.Length == 0)
            {
                return;
            }

            TokenUserInfo token = await userContextReader.GetTokenUserInfoFromContextThrowIfNull();

            if (token.RoleEnum == null)
            {
                throw new InvalidTokenException("Role is missing in token.");
            }

            if (allowedRoles.Contains(token.RoleEnum.Value) == false)
            {
                throw new UnauthorizedAccessToServiceException(localizationService);
            }
        }
    }
}
