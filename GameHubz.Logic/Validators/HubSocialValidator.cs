using FluentValidation;

namespace GameHubz.Logic.Validators
{
    public class HubSocialValidator : AbstractValidator<HubSocialEntity>
    {
        public HubSocialValidator(ILocalizationService localizationService)
        {
            this.RuleFor(x => x.Username)
                .NotEmpty();
            this.RuleFor(x => x.Type)
                .NotEmpty();
        }
    }
}