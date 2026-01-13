using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using GameHubz.DataModels.Config;
using GameHubz.DataModels.Models;
using GameHubz.DataModels.Tokens;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;

namespace GameHubz.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private const string loginRoute = "/login";

        private readonly AuthSettings authSettings;
        private readonly AuthService authService;
        private readonly GoogleAuthService googleAuthService;
        private readonly ILocalizationService localizationService;
        private readonly PasswordManagementService passwordManagementService;
        private readonly UserService userService;
        private readonly IConfiguration configuration;

        public AuthController(AuthService authService,
            IOptions<AuthSettings> authSettingsOptions,
            ILocalizationService localizationService,
            GoogleAuthService googleAuthService,
            PasswordManagementService passwordManagementService,
            UserService userService,
            IConfiguration configuration)
        {
            if (authSettingsOptions is null)
            {
                throw new ArgumentNullException(nameof(authSettingsOptions));
            }

            this.authSettings = authSettingsOptions.Value;
            this.authService = authService;
            this.localizationService = localizationService;
            this.googleAuthService = googleAuthService;
            this.passwordManagementService = passwordManagementService;
            this.userService = userService;
            this.configuration = configuration;
        }

        [HttpPost("login")]
        public async Task<ActionResult> Login([FromBody] LoginRequestDto loginRequest)
        {
            if (loginRequest is null)
            {
                return this.BadRequest("Missing login data");
            }

            if (!this.ModelState.IsValid)
            {
                return this.BadRequest(this.ModelState);
            }

            TokenResponse response = await this.authService.Login(loginRequest);

            if (response.IsSuccessful)
            {
                return this.Ok(response);
            }
            else
            {
                return this.Unauthorized(response.Messages);
            }
        }

        [HttpPost("googleLogin")]
        [Authorize]
        public async Task<ActionResult> LoginGoogle()
        {
            await this.googleAuthService.GoogleLogin();

            return this.Ok();
        }

        [HttpPost("refreshtoken")]
        public async Task<ActionResult> RefreshToken([FromBody] TokenRequest tokenRequest)
        {
            if (tokenRequest == null)
            {
                return this.BadRequest("Token is missing");
            }

            if (!this.ModelState.IsValid)
            {
                return this.BadRequest(this.ModelState);
            }

            TokenResponse? response = await this.authService.ExchangeRefreshToken(tokenRequest, this.authSettings.SecretKey);

            if (response != null)
            {
                if (response.IsSuccessful &&
                                response.AccessToken != null &&
                                response.RefreshToken != null)
                {
                    return this.Ok(new TokenResponse(response.AccessToken, response.RefreshToken));
                }
                else
                {
                    return this.BadRequest(response.Messages);
                }
            }

            return this.BadRequest(this.localizationService["Error.TokenCannotBeCreated"]);
        }

        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout(
            [FromBody] string refreshToken)
        {
            await this.authService.Logout(refreshToken);
            return this.Ok();
        }

        [HttpPost("setPassword")]
        [Authorize]
        public async Task<IActionResult> SetPassword([FromBody] UserPasswordEdit userPasswordEdit)
        {
            if (userPasswordEdit == null)
            {
                return this.BadRequest("Missing set password data");
            }

            await this.authService.SetUserPassword(userPasswordEdit);
            return this.Ok();
        }

        [HttpPost("forgotPassword")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto forgotPasswordRequest)
        {
            await this.passwordManagementService.CreateForgotPasswordToken(forgotPasswordRequest);

            return Ok(this.localizationService["Password.ForgotPassword"]);
        }

        [HttpPost("resetPassword")]
        public async Task ResetPassword([FromBody] ResetPasswordRequestDto resetPasswordRequest)
        {
            await this.passwordManagementService.ResetPassword(resetPasswordRequest);

            this.Response.Redirect($"{configuration.GetStringThrowIfNull("Frontend:BaseUrl")}{loginRoute}");
        }

        [HttpPost("registerUser")]
        public async Task RegisterUser([FromBody] RegisterUserPostDto registerUserPostDto)
        {
            await this.userService.RegisterUser(registerUserPostDto);
        }

        [HttpGet("verifyEmail")]
        public async Task VerifyEmail([FromQuery] Guid verifyEmailToken)
        {
            await this.userService.VerifyEmail(verifyEmailToken);

            this.Response.Redirect($"{configuration.GetStringThrowIfNull("Frontend:BaseUrl")}{loginRoute}");
        }

        [HttpPost("resendVerificationEmail")]
        public async Task ResendVerificationEmail([FromBody] ResendVerificationRequestDto resendVerificationRequestDto)
        {
            await this.userService.ResendVerificationEmail(resendVerificationRequestDto);
        }

        [HttpPost("changePassword")]
        public async Task ChangePassword(string newPassword)
        {
            await this.authService.ChangeUserPassword(newPassword);
        }
    }
}
