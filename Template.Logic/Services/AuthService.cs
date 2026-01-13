using System.Security.Claims;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Template.DataModels.Config;
using Template.DataModels.Tokens;

namespace Template.Logic.Services
{
    public class AuthService : AppBaseService
    {
        private readonly IAccessTokenHandler accessTokenHandler;
        private readonly IAccessTokenFactory accessTokenFactory;
        private readonly IRefreshTokenFactory refreshTokenFactory;
        private readonly IConfiguration configuration;
        private readonly DateTimeProvider dateTimeProvider;
        private readonly AuthSettings authSettings;
        private readonly UserService userService;
        private readonly AnonymousUserContextReader anonymousUserContextReader;
        private readonly IValidator<LoginRequestDto> validator;
        private readonly IPasswordHasher passwordHasher;

        public AuthService(
            IUnitOfWorkFactory factory,
            ILocalizationService localizationService,
            IAccessTokenHandler accessTokenHandler,
            IAccessTokenFactory accessTokenFactory,
            IRefreshTokenFactory refreshTokenFactory,
            IConfiguration configuration,
            IOptions<AuthSettings> authSettingsOptions,
            DateTimeProvider dateTimeProvider,
            UserService userService,
            IUserContextReader userContextReader,
            AnonymousUserContextReader anonymousUserContextReader,
            IValidator<LoginRequestDto> validator,
            IPasswordHasher passwordHasher
            )
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.accessTokenHandler = accessTokenHandler;
            this.accessTokenFactory = accessTokenFactory;
            this.refreshTokenFactory = refreshTokenFactory;
            this.configuration = configuration;
            this.dateTimeProvider = dateTimeProvider;
            this.authSettings = authSettingsOptions.Value;
            this.userService = userService;
            this.anonymousUserContextReader = anonymousUserContextReader;
            this.validator = validator;
            this.passwordHasher = passwordHasher;
        }

        public async Task<TokenResponse> ExchangeRefreshToken(TokenRequest tokenRequest, string signingKey)
        {
            ClaimsPrincipal claimsPrincipal = this.accessTokenHandler.GetPrincipalFromToken(tokenRequest.AccessToken, signingKey);

            if (claimsPrincipal == null)
            {
                throw new ParameterException(this.LocalizationService, "Exception.ParameterExceptionInvalidToken", nameof(claimsPrincipal));
            }

            Claim id = claimsPrincipal.Claims.First(c => c.Type == "id");

            if (!Guid.TryParse(id.Value, out Guid idParsed))
            {
                throw new ParameterException(this.LocalizationService, "Exception.ParameterExceptionInvalidToken", nameof(id));
            }

            UserEntity? user = await this.AppUnitOfWork.UserRepository.GetByIdWithRefreshTokens(idParsed);

            if (user == null)
            {
                if (Guid.TryParse(id.Value, out Guid realId))
                {
                    throw new EntityNotFoundException(realId, nameof(UserEntity), this.LocalizationService);
                }
                else
                {
                    throw new EntityNotFoundException(Guid.Empty, nameof(UserEntity), this.LocalizationService);
                }
            }

            if (!this.HasValidRefreshToken(user, tokenRequest.RefreshToken))
            {
                throw new InvalidTokenException();
            }

            AccessToken accessToken = await this.accessTokenFactory.GenerateEncodedToken(CreateTokenUserInfo(user));

            await this.DeleteTokenByUserId(user.Id!.Value, tokenRequest.RefreshToken);

            string refreshToken = await this.GenerateAndAddRefreshTokenForUser(user.Id.Value);

            return new TokenResponse(accessToken, refreshToken);
        }

        public async Task ChangeUserPassword(string newPassword)
        {
            var token = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            await this.SetUserNewPassword(token!.UserId, newPassword);
        }

        public async Task SetUserPassword(UserPasswordEdit userPasswordEditDto)
        {
            await this.SetUserNewPassword(userPasswordEditDto.Id, userPasswordEditDto.NewPassword);
        }

        public async Task<TokenResponse> Login(LoginRequestDto loginRequest)
        {
            this.validator.ValidateAndThrow(loginRequest);

            UserEntity? user = await this.AppUnitOfWork.UserRepository.GetByEmail(loginRequest.Email);

            if (user == null
                || this.HashPassword(loginRequest.Password, user.PasswordNonce) != user.Password)
            {
                return new TokenResponse(false, null, this.LocalizationService["AuthService.InvalidUsernameOrPassword"]);
            }

            var refreshToken = await this.GenerateAndAddRefreshTokenForUser(user.Id!.Value);

            var accessToken = await this.accessTokenFactory.GenerateEncodedToken(CreateTokenUserInfo(user));

            UserDto userDto = await this.userService.GetEntityById(user.Id.Value);

            if (userDto == null)
            {
                throw new EntityNotFoundException(user.Id.Value, nameof(UserEntity), this.LocalizationService);
            }

            if (!user.IsVerified)
            {
                return new TokenResponse(false);
            }

            return new TokenResponse(accessToken, refreshToken, user: userDto, true);
        }

        public async Task Logout(string refreshTokenValue)
        {
            if (string.IsNullOrWhiteSpace(refreshTokenValue))
            {
                throw new ParameterException(this.LocalizationService, "Exception.ParameterExceptionInvalidToken", nameof(refreshTokenValue));
            }

            RefreshTokenEntity? refreshToken = this.AppUnitOfWork.RefreshTokenRepository.FindByTokenValue(refreshTokenValue);

            if (refreshToken != null)
            {
                await this.AppUnitOfWork.RefreshTokenRepository.HardDeleteEntity(refreshToken);

                await this.SaveAsync();
            }
        }

        private string HashPassword(string password, string salt)
        {
            return this.passwordHasher.HashPassword(password, salt);
        }

        private bool HasValidRefreshToken(UserEntity user, string refreshToken)
        {
            return user.RefreshTokens.Any(rt => rt.Token == refreshToken && this.dateTimeProvider.Now() <= rt.Expires);
        }

        private async Task<string> GenerateAndAddRefreshTokenForUser(Guid userId)
        {
            if (!int.TryParse(this.configuration["RefreshTokenTimeoutHours"], out int refreshTokenTimeoutHours))
            {
                throw new InvalidCastException("Invalid configuration");
            }

            string refreshTokenValue = this.refreshTokenFactory.GenerateToken();

            RefreshTokenEntity refreshToken = new()
            {
                Id = Guid.NewGuid(),
                Expires = this.dateTimeProvider.Now().AddHours(refreshTokenTimeoutHours),
                Token = refreshTokenValue,
                UserId = userId
            };

            await this.AppUnitOfWork.RefreshTokenRepository.AddEntity(refreshToken, this.anonymousUserContextReader);

            await this.SaveAsync();

            return refreshTokenValue;
        }

        private async Task DeleteTokenByUserId(Guid userId, string token)
        {
            RefreshTokenEntity? refreshToken = this.AppUnitOfWork.RefreshTokenRepository.FindByUserIdAndTokenValue(userId, token);

            if (refreshToken == null)
            {
                throw new EntityNotFoundException($"userId: {userId}, token: {token}", nameof(RefreshTokenEntity), this.LocalizationService);
            }

            await this.AppUnitOfWork.RefreshTokenRepository.HardDeleteEntity(refreshToken);
            await this.SaveAsync();
        }

        private static TokenUserInfo CreateTokenUserInfo(UserEntity user)
        {
            return new TokenUserInfo()
            {
                UserId = user.Id!.Value,
                Username = user.FirstName,
                Role = user.UserRole.SystemName,
            };
        }

        private async Task SetUserNewPassword(Guid userId, string newPassword)
        {
            UserEntity user = await this.AppUnitOfWork.UserRepository.GetByIdOrThrowIfNull(userId);

            user.Password = this.HashPassword(newPassword, user.PasswordNonce);

            await this.AppUnitOfWork.UserRepository.UpdateEntity(user, this.UserContextReader);

            await this.SaveAsync();
        }
    }
}