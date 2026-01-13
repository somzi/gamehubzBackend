using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using GameHubz.Common.Consts;
using GameHubz.Logic.Crypto;

namespace GameHubz.Logic.Services
{
    public class GoogleAuthService : AppBaseService
    {
        private readonly IHttpContextAccessor httpContextAccessor;
        private readonly UserService userService;

        public GoogleAuthService(
            IUnitOfWorkFactory factory,
            ILocalizationService localizationService,
            IHttpContextAccessor httpContextAccessor,
            IUserContextReader userContextReader,
            UserService userService)
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.httpContextAccessor = httpContextAccessor;
            this.userService = userService;
        }

        public async Task GoogleLogin()
        {
            ClaimsPrincipal claimsPrincipal = this.httpContextAccessor.HttpContext.User;

            Claim email = this.GetClaim(claimsPrincipal, ClaimTypes.Email);

            UserEntity? user = await this.AppUnitOfWork.UserRepository.ShallowGetByEmail(email.Value);

            if (user != null)
            {
                return;
            }

            Claim firstName = this.GetClaim(claimsPrincipal, ClaimTypes.Name);

            Claim lastname = this.GetClaim(claimsPrincipal, ClaimTypes.Surname);

            UserEntity newUserEntity = new()
            {
                FirstName = firstName.Value,
                LastName = lastname.Value,
                Email = email.Value,
                Password = "",
                UserRoleId = UserRoles.BasicUser,
                IsNativeAuthentication = false,
                IsVerified = true,
                PasswordNonce = NonceGenerator.GetNew()
            };

            await userService.AddUpdateUserAnonymously(newUserEntity);
        }

        private Claim GetClaim(ClaimsPrincipal claimsPrincipal, string claimType)
        {
            Claim? claim = claimsPrincipal.FindFirst(claimType);

            if (claim == null)
            {
                throw new GoogleTokenParameterException(this.LocalizationService, claimType);
            }

            return claim;
        }
    }
}
