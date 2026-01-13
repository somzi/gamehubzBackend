using FluentValidation;
using GameHubz.DataModels.Api;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Services;

namespace GameHubz.Logic.Queuing.Services.LocalQueueServices
{
    public class LocalQueueEmailService : AppBaseService
    {
        private readonly IMapper mapper;
        private readonly IValidator<EmailQueueEntity> validator;
        private readonly AnonymousUserContextReader anonymousUserContextReader;

        public LocalQueueEmailService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IUserContextReader userContextReader,
            AnonymousUserContextReader anonymousUserContextReader,
            IValidator<EmailQueueEntity> validator)
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.mapper = mapper;
            this.anonymousUserContextReader = anonymousUserContextReader;
            this.validator = validator;
        }

        public async Task QueueEmail(EmailQueueModel? emailQueue)
        {
            if (emailQueue is null)
            {
                throw new ArgumentNullException(nameof(emailQueue));
            }

            EmailQueueEntity emailQueueEntity = this.mapper.Map<EmailQueueEntity>(emailQueue);

            var result = this.validator.Validate(emailQueueEntity);

            if (!result.IsValid)
            {
                throw new ValidationException(this.LocalizationService["EmailQueueValidator.GeneralErrorMessage"], result.Errors);
            }

            emailQueueEntity.Status = EmailQueueStatus.Pending;
            await this.AppUnitOfWork.EmailQueueRepository.AddEntity(emailQueueEntity, this.anonymousUserContextReader);

            await this.SaveAsync();
        }
    }
}
