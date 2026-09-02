using GameHubz.DataModels.Api;
using GameHubz.Logic.Queuing.Queues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
        private readonly ICacheService cacheService;
        private readonly AuthThrottleService authThrottleService;
        private readonly ILogger<PasswordManagementService> logger;

        private const int ForgotPasswrodTokenExpireHours = 1;

        // Brute-force guard for the 6-digit reset OTP (F3): max wrong guesses per account before the
        // code must be re-requested, and the window over which those attempts are counted.
        private const int MaxOtpResetAttempts = 5;
        private static readonly TimeSpan OtpAttemptWindow = TimeSpan.FromMinutes(30);

        public PasswordManagementService(
            IUnitOfWorkFactory factory,
            ILocalizationService localizationService,
            IUserContextReader userContextReader,
            UserService userService,
            IConfiguration configuration,
            IPasswordHasher passwordHasher,
            EmailQueue emailQueue,
            DateTimeProvider dateTimeProvider,
            EmailService emailService,
            ICacheService cacheService,
            AuthThrottleService authThrottleService,
            ILogger<PasswordManagementService> logger)
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.authThrottleService = authThrottleService;
            this.logger = logger;
            this.userService = userService;
            this.configuration = configuration;
            this.passwordHasher = passwordHasher;
            this.emailQueue = emailQueue;
            this.dateTimeProvider = dateTimeProvider;
            this.emailService = emailService;
            this.cacheService = cacheService;
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

            // Invalidate all existing sessions on a password reset (see AuthService.ChangeUserPassword).
            await this.AppUnitOfWork.RefreshTokenRepository.HardDeleteAllByUserId(user.Id!.Value);
            await this.SaveAsync();
        }

        public async Task ResetPasswordWithOtp(ResetPasswordOtpRequestDto resetPasswordRequestDto)
        {
            if (resetPasswordRequestDto.Password != resetPasswordRequestDto.ConfirmPassword)
            {
                throw new PasswordNotMatchingException(this.LocalizationService);
            }

            // Brute-force guard (F3): the OTP is only 6 digits, so cap wrong guesses per account within
            // the validity window. Tracked in the cache (no schema change); keyed by the target email.
            string normalizedEmail = (resetPasswordRequestDto.Email ?? string.Empty).Trim().ToLowerInvariant();
            string attemptsKey = $"pwd_reset_otp_attempts:{normalizedEmail}";
            int attempts = await this.cacheService.GetAsync<int?>(attemptsKey) ?? 0;
            if (attempts >= MaxOtpResetAttempts)
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.TooManyResetAttempts"]);
            }

            UserEntity? user = await this.AppUnitOfWork.UserRepository.GetByOtpAndMail(resetPasswordRequestDto);

            if (user == null)
            {
                await this.cacheService.SetAsync<int?>(attemptsKey, attempts + 1, OtpAttemptWindow);
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

            // Reset succeeded: clear the attempt counter and invalidate all existing sessions
            // (see AuthService.ChangeUserPassword).
            await this.cacheService.RemoveAsync(attemptsKey);
            await this.AppUnitOfWork.RefreshTokenRepository.HardDeleteAllByUserId(user.Id!.Value);
            await this.SaveAsync();
        }

        public async Task CreateForgotPasswordToken(ForgotPasswordRequestDto forgotPasswordRequest)
        {
            if (string.IsNullOrEmpty(forgotPasswordRequest.Email))
            {
                throw new EmptyEmailException(this.LocalizationService);
            }

            forgotPasswordRequest.Email = forgotPasswordRequest.Email.Trim().ToLowerInvariant();

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
                Subject = this.LocalizationService.ForRecipient(user.Language, "Email.ResetPassword.Subject"),
                Message = message,
                IsMessageHtml = true
            };

            await this.emailQueue.Enqueue(emailQueue);
        }

        // NOTE (F1): the former SendEmailWithForgotPasswordToken(string) overload generated the reset
        // OTP and RETURNED it to the caller (the controller forwarded it in the HTTP response — a full
        // account-takeover primitive). It has been removed; all forgot-password flows now go through
        // SendForgotPasswordOtp below, which delivers the code only by email.

        /// <summary>
        /// Secure forgot-password entry point used by both the legacy and v2 forgotPassword routes:
        /// (1) never returns the OTP in the response — the code is delivered only by email, and
        /// (2) does not reveal whether an account exists for the address, so the endpoint can't be
        /// used to enumerate registered emails. A missing user is a silent no-op.
        /// </summary>
        public async Task SendForgotPasswordOtp(string email)
        {
            email = (email ?? string.Empty).Trim().ToLowerInvariant();

            // Caps how often one mailbox can be targeted: unthrottled, this endpoint is an
            // inbox-bombing primitive and lets an attacker keep churning a victim's pending OTP.
            // Blocked sends return silently, exactly like the unknown-address case below, so the
            // anti-enumeration property documented above (always 200, never confirms an account)
            // holds either way.
            if (await this.authThrottleService.IsResetSendBlockedAsync(email))
            {
                return;
            }

            var user = await AppUnitOfWork.UserRepository.GetByEmail(email);
            if (user == null)
            {
                return;
            }

            // Counted only once we know a mail is actually going out, so probing addresses that
            // don't exist can't burn a real user's budget.
            await this.authThrottleService.RegisterResetSendAsync(email);

            int otpNumber = RandomNumberGenerator.GetInt32(100000, 1000000);
            string otpCode = otpNumber.ToString("000 000");

            user.ForgotPasswordOtp = otpNumber.ToString();
            user.ForgotPasswordTokenExpires = DateTime.UtcNow.AddMinutes(30);
            await this.userService.AddUpdateUserAnonymously(user);

            await SendMailForResetPassword(otpCode, email, user.Language);
        }

        private async Task SendMailForResetPassword(string otpCode, string email, string? recipientLanguage)
        {
            string subject = this.LocalizationService.ForRecipient(recipientLanguage, "Email.ResetOtp.Subject");

            // Sentences come from the resx, the markup stays here — a translator can reword the
            // copy without ever being able to break the layout.
            string greeting = this.LocalizationService.ForRecipient(recipientLanguage, "Email.ResetOtp.Greeting");
            string intro = this.LocalizationService.ForRecipient(recipientLanguage, "Email.ResetOtp.Intro");
            string duration = this.LocalizationService.ForRecipient(recipientLanguage, "Email.ResetOtp.ValidityDuration");
            string validity = this.LocalizationService.ForRecipient(
                recipientLanguage,
                "Email.ResetOtp.Validity",
                $@"<strong style=""color: #e2e8f0;"">{duration}</strong>");
            string ignore = this.LocalizationService.ForRecipient(recipientLanguage, "Email.ResetOtp.Ignore");

            string message = $@"
            <div style=""font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; max-width: 600px; margin: 0 auto; background-color: #0f172a; border-radius: 16px; overflow: hidden; box-shadow: 0 4px 6px rgba(0,0,0,0.1);"">

                <div style=""background-color: #1e293b; padding: 24px; text-align: center; border-bottom: 1px solid #334155;"">
                    <h1 style=""margin: 0; color: #10B981; font-size: 28px; letter-spacing: 1px;"">GAMEHUBZ</h1>
                </div>

                <div style=""padding: 32px; color: #f8fafc;"">
                    <p style=""font-size: 16px; line-height: 1.5; margin-top: 0;"">{greeting}</p>
                    <p style=""font-size: 16px; line-height: 1.5; color: #cbd5e1;"">
                        {intro}
                    </p>

                    <div style=""text-align: center; margin: 40px 0;"">
                        <span style=""display: inline-block; font-size: 36px; font-weight: bold; letter-spacing: 8px; color: #ffffff; background-color: #334155; padding: 16px 32px; border-radius: 12px; border: 2px solid #10B981;"">
                            {otpCode}
                        </span>
                    </div>

                    <p style=""font-size: 14px; text-align: center; color: #94a3b8;"">
                        {validity}
                    </p>
                </div>

                <div style=""background-color: #0b1120; padding: 24px; text-align: center;"">
                    <p style=""margin: 0; font-size: 12px; color: #64748b; line-height: 1.5;"">
                        {ignore}
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
                this.logger.LogError(ex, "Failed to send the password reset code.");
            }
        }
    }
}