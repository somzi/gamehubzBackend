using FluentValidation;

namespace GameHubz.Logic.Validators
{
    public class EmailQueueValidator : AbstractValidator<EmailQueueEntity>
    {
        private readonly ILocalizationService localizationService;

        public EmailQueueValidator(ILocalizationService localizationService)
        {
            this.localizationService = localizationService;

            this.RuleFor(x => x.To)
                .NotEmpty().WithMessage(this.localizationService["EmailQueueValidator.ToIsEmpty"]);
        }
    }
}
