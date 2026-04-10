using GameHubz.DataModels.Api;
using GameHubz.Logic.Queuing.Queues;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;

namespace GameHubz.Logic.Services
{
    public class PasswordManagementService : AppBaseService
    {
        private readonly UserService userService;
        private readonly IConfiguration configuration;
        private readonly IPasswordHasher passwordHasher;
        private readonly EmailQueue emailQueue;
        private readonly DateTimeProvider dateTimeProvider;
        private readonly EmailService emailService;

        private const int ForgotPasswrodTokenExpireHours = 1;

        public PasswordManagementService(
            IUnitOfWorkFactory factory,
            ILocalizationService localizationService,
            IUserContextReader userContextReader,
            UserService userService,
            IConfiguration configuration,
            IPasswordHasher passwordHasher,
            EmailQueue emailQueue,
            DateTimeProvider dateTimeProvider,
            EmailService emailService)
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.userService = userService;
            this.configuration = configuration;
            this.passwordHasher = passwordHasher;
            this.emailQueue = emailQueue;
            this.dateTimeProvider = dateTimeProvider;
            this.emailService = emailService;
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

        public async Task ResetPasswordWithOtp(ResetPasswordOtpRequestDto resetPasswordRequestDto)
        {
            if (resetPasswordRequestDto.Password != resetPasswordRequestDto.ConfirmPassword)
            {
                throw new PasswordNotMatchingException(this.LocalizationService);
            }

            UserEntity? user = await this.AppUnitOfWork.UserRepository.GetByOtpAndMail(resetPasswordRequestDto);

            if (user == null)
            {
                throw new EntityNotFoundException("Forgot Password Token", "UserEntity", this.LocalizationService);
            }

            if (user.ForgotPasswordTokenExpires < this.dateTimeProvider.Now())
            {
                throw new InvalidForgotPasswordTokenException(this.LocalizationService);
            }

            user.IsVerified = true;
            user.ForgotPasswordOtp = null;
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

        public async Task<string> SendEmailWithForgotPasswordToken(string email)
        {
            var user = await AppUnitOfWork.UserRepository.GetByEmail(email) ?? throw new Exception("This email does not exists.");

            int otpNumber = RandomNumberGenerator.GetInt32(100000, 1000000);
            string otpCode = otpNumber.ToString("000 000");
            string otpForDatabase = otpNumber.ToString();

            user.ForgotPasswordOtp = otpForDatabase;
            user.ForgotPasswordTokenExpires = DateTime.UtcNow.AddMinutes(30);
            await this.userService.AddUpdateUserAnonymously(user);

            await SendMailForResetPassword(otpCode, email);

            return otpForDatabase;
        }

        private async Task SendMailForResetPassword(string otpCode, string email)
        {
            string subject = "GameHubz: Your password reset code";

            string message = $@"
            <div style=""font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; max-width: 600px; margin: 0 auto; background-color: #0f172a; border-radius: 16px; overflow: hidden; box-shadow: 0 4px 6px rgba(0,0,0,0.1);"">

                <div style=""background-color: #1e293b; padding: 24px; text-align: center; border-bottom: 1px solid #334155;"">
                    <h1 style=""margin: 0; color: #10B981; font-size: 28px; letter-spacing: 1px;"">GAMEHUBZ</h1>
                </div>

                <div style=""padding: 32px; color: #f8fafc;"">
                    <p style=""font-size: 16px; line-height: 1.5; margin-top: 0;"">Hello,</p>
                    <p style=""font-size: 16px; line-height: 1.5; color: #cbd5e1;"">
                        We received a request to reset the password for your account. Enter the code below in the app to set a new password:
                    </p>

                    <div style=""text-align: center; margin: 40px 0;"">
                        <span style=""display: inline-block; font-size: 36px; font-weight: bold; letter-spacing: 8px; color: #ffffff; background-color: #334155; padding: 16px 32px; border-radius: 12px; border: 2px solid #10B981;"">
                            {otpCode}
                        </span>
                    </div>

                    <p style=""font-size: 14px; text-align: center; color: #94a3b8;"">
                        This code is valid for the next <strong style=""color: #e2e8f0;"">15 minutes</strong>.
                    </p>
                </div>

                <div style=""background-color: #0b1120; padding: 24px; text-align: center;"">
                    <p style=""margin: 0; font-size: 12px; color: #64748b; line-height: 1.5;"">
                        If you didn't request a password reset, you can safely ignore this email. Your account is secure and no one can access it without this code.
                    </p>
                </div>
            </div>";

            try
            {
                var emailModel = new EmailModel
                {
                    To = email,
                    Subject = subject,
                    Message = message,
                    IsMessageHtml = true,
                    Cc = ""
                };

                await emailService.SendEmail(emailModel);
            }
            catch (Exception ex)
            {
                throw new Exception($"Greška pri slanju: {ex.Message}");
            }
        }
    }
}