using Microsoft.Extensions.Logging;
using Template.DataModels.Enums;
using Template.Logic.Queuing.Services.LocalQueueServices;
using Template.Logic.Services;

namespace Template.Logic.Queuing.Consumers.LocalQueueConsumers
{
    public class LocalQueueEmailConsumer : AppBaseService
    {
        private readonly IMapper mapper;
        private readonly EmailService emailService;
        private readonly ILogger<LocalQueueEmailService> logger;
        private readonly AnonymousUserContextReader anonymousUserContextReader;

        public LocalQueueEmailConsumer(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            EmailService emailService,
            ILogger<LocalQueueEmailService> logger,
            IUserContextReader userContextReader,
            AnonymousUserContextReader anonymousUserContextReader
            )
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.mapper = mapper;
            this.emailService = emailService;
            this.logger = logger;
            this.anonymousUserContextReader = anonymousUserContextReader;
        }

        public async Task SendNext()
        {
            EmailQueueEntity? emailQueueEntity;
            int sendMax = 10;
            int sendCount = 0;

            while (((emailQueueEntity = await this.AppUnitOfWork.EmailQueueRepository.GetNextEmailQueue()) != null) && sendMax > sendCount)
            {
                if (emailQueueEntity.Id == null)
                {
                    this.logger.LogError("Error while sending email, 'emailQueueEntity' is null.");
                    sendCount++;
                    continue;
                }

                try
                {
                    EmailModel emailModel = this.mapper.Map<EmailModel>(emailQueueEntity);

                    await this.emailService.SendEmail(emailModel);

                    await this.UpdateStatus((Guid)emailQueueEntity.Id, EmailQueueStatus.Completed);
                }
                catch (Exception ex)
                {
                    await this.LogSendingError(emailQueueEntity.Id.Value, ex);
                }

                sendCount++;
            }
        }

        private async Task UpdateStatus(
            Guid emailQueueId,
            EmailQueueStatus emailQueueStatus,
            string? error = null)
        {
            EmailQueueEntity? emailQueueEntity = await this.AppUnitOfWork.EmailQueueRepository.GetByIdOrThrowIfNull(emailQueueId);

            emailQueueEntity.Status = emailQueueStatus;

            if (error != null)
            {
                emailQueueEntity.Error = error;
            }

            await this.AppUnitOfWork.EmailQueueRepository.UpdateEntity(emailQueueEntity, this.anonymousUserContextReader);

            await this.SaveAsync();
        }

        private async Task LogSendingError(Guid approvalQueueEnityId, Exception ex)
        {
            this.logger.LogError(ex, "Failed to send email");

            await this.UpdateStatus(approvalQueueEnityId, EmailQueueStatus.Error, ex.ToString());
        }
    }
}