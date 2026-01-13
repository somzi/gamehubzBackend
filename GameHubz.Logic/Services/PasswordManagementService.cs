using Microsoft.Extensions.Configuration;
using GameHubz.DataModels.Api;
using GameHubz.Logic.Queuing.Queues;

namespace GameHubz.Logic.Services
{
    public class PasswordManagementService : AppBaseService
    {
        private readonly UserService userService;
        private readonly IConfiguration configuration;
        private readonly IPasswordHasher passwordHasher;
        private readonly EmailQueue emailQueue;
        private readonly DateTimeProvider dateTimeProvider;

        private const int ForgotPasswrodTokenExpireHours = 1;

        public PasswordManagementService(
            IUnitOfWorkFactory factory,
            ILocalizationService localizationService,
            IUserContextReader userContextReader,
            UserService userService,
            IConfiguration configuration,
            IPasswordHasher passwordHasher,
            EmailQueue emailQueue,
            DateTimeProvider dateTimeProvider)
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.userService = userService;
            this.configuration = configuration;
            this.passwordHasher = passwordHasher;
            this.emailQueue = emailQueue;
            this.dateTimeProvider = dateTimeProvider;
        }

        public async Task ResetPassword(ResetPasswordRequestDto resetPasswordRequestDto)
        {
            if (resetPasswordRequestDto.Password != resetPasswordRequestDto.ConfirmPassword)
            {
                throw new PasswordNotMatchingException(this.LocalizationService);
            }

            UserEntity? user = await this.AppUnitOfWork.UserRepository.GetByForgotPasswordToken(resetPasswordRequestDto.ForgotPasswordToken);

            if (user == null)
            {
                throw new EntityNotFoundException("Forgot Password Token", "UserEntity", this.LocalizationService);
            }

            if (user.ForgotPasswordTokenExpires < this.dateTimeProvider.Now())
            {
                throw new InvalidForgotPasswordTokenException(this.LocalizationService);
            }

            user.IsVerified = true;
            user.ForgotPasswordToken = null;
            user.ForgotPasswordTokenExpires = null;
            user.Password = this.passwordHasher.HashPassword(resetPasswordRequestDto.Password, user.PasswordNonce);

            await this.userService.AddUpdateUserAnonymously(user);
        }

        public async Task CreateForgotPasswordToken(ForgotPasswordRequestDto forgotPasswordRequest)
        {
            if (string.IsNullOrEmpty(forgotPasswordRequest.Email))
            {
                throw new EmptyEmailException(this.LocalizationService);
            }

            UserEntity? user = await this.AppUnitOfWork.UserRepository.ShallowGetByEmail(forgotPasswordRequest.Email);

            if (user == null)
            {
                throw new EntityNotFoundException("Email", "UserEntity", this.LocalizationService);
            }

            user.ForgotPasswordToken = Guid.NewGuid();
            user.ForgotPasswordTokenExpires = this.dateTimeProvider.Now().AddHours(ForgotPasswrodTokenExpireHours);

            await this.userService.AddUpdateUserAnonymously(user);

            await this.SendEmailWithForgotPasswordToken(user);
        }

        public async Task SendEmailWithForgotPasswordToken(UserEntity user)
        {
            string route = "/forgotpassword";
            string message = $"{configuration.GetStringThrowIfNull("Frontend:BaseUrl")}{route}?forgotPasswordToken={user.ForgotPasswordToken}";

            EmailQueueModel emailQueue = new()
            {
                To = user.Email,
                Subject = "Reset Password",
                Message = message,
                IsMessageHtml = true
            };

            await this.emailQueue.Enqueue(emailQueue);
        }
    }
}
