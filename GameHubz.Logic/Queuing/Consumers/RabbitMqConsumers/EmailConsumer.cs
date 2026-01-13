using Microsoft.Extensions.DependencyInjection;
using GameHubz.DataModels.Config.RabbitMqConfig;
using GameHubz.Logic.Services;

namespace GameHubz.Logic.Queuing.Consumers.RabbitMqConsumers
{
    public class EmailConsumer : BaseConsumer<EmailModel>
    {
        private readonly EmailService emailService;

        public EmailConsumer(IServiceProvider serviceProvider, QueueConfig queueConfig, Queue queue)
            : base(serviceProvider, queueConfig, queue)
        {
            using var scope = serviceProvider.CreateScope();

            EmailService? emailService = scope.ServiceProvider.GetService<EmailService>();

            if (emailService == null)
            {
                throw new UnavaliableServiceException(this.LocalizationService);
            }

            this.emailService = emailService;
        }

        protected override async Task OnReceivedMessage(EmailModel model)
        {
            await this.emailService.SendEmail(model);
        }
    }
}
