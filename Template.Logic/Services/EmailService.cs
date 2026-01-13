using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Template.DataModels.Config;

namespace Template.Logic.Services
{
    public class EmailService
    {
        private readonly ILogger<EmailService> logger;
        private readonly SmtpOptions smtpOptions;

        public EmailService(
            ILogger<EmailService> logger,
            IOptions<SmtpOptions> smtpOptionsConfig)
        {
            this.logger = logger;
            this.smtpOptions = smtpOptionsConfig.Value;
        }

        public async Task SendEmail(EmailModel emailModel)
        {
            if (emailModel is null)
            {
                throw new ArgumentNullException(nameof(emailModel));
            }

            this.logger.LogTrace($"Sending email to: {emailModel.To}");

            using SmtpClient client = this.CreateSmtpClient();

            MailAddress? mailAddressFrom = CreateFromAddress();

            List<string>? toEmails = CreateToEmails(emailModel);

            MailMessage message = CreateMailMessage(emailModel, mailAddressFrom, toEmails);

            await client.SendMailAsync(message);
        }

        private SmtpClient CreateSmtpClient()
        {
            SmtpClient client = new(this.smtpOptions.Host, this.smtpOptions.Port)
            {
                UseDefaultCredentials = false,
                EnableSsl = true,
                Credentials = new System.Net.NetworkCredential(
                    this.smtpOptions.Username,
                    this.smtpOptions.Password)
            };

            return client;
        }

        private MailAddress CreateFromAddress()
        {
            MailAddress mailAddressFrom;

            try
            {
                mailAddressFrom = new(
                    this.smtpOptions.Username,
                    this.smtpOptions.Username,
                    System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                throw new MailServiceException("Create from address", ex);
            }

            return mailAddressFrom;
        }

        private static List<string> CreateToEmails(EmailModel emailModel)
        {
            List<string> toEmails = StringSeparatedMailParser.GetEmailsFromString(emailModel.To);

            if (!toEmails.Any())
            {
                throw new MailServiceException("No email was able to parse from TO field.");
            }

            return toEmails;
        }

        private static MailMessage CreateMailMessage(
            EmailModel emailModel,
            MailAddress mailAddressFrom,
            List<string> toEmails)
        {
            MailMessage message = new()
            {
                From = mailAddressFrom,

                IsBodyHtml = emailModel.IsMessageHtml,
                SubjectEncoding = System.Text.Encoding.UTF8,
                BodyEncoding = System.Text.Encoding.UTF8,

                Body = emailModel.Message,
                Subject = emailModel.Subject
            };

            toEmails.ForEach(x => message.To.Add(new MailAddress(x)));

            List<string> ccEmails
                = string.IsNullOrEmpty(emailModel.Cc) ? new() : StringSeparatedMailParser.GetEmailsFromString(emailModel.Cc);

            ccEmails.ForEach(x => message.CC.Add(x));

            return message;
        }
    }
}