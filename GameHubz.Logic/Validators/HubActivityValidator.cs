using FluentValidation;

namespace GameHubz.Logic.Validators
{
    public class HubActivityValidator : AbstractValidator<HubActivityEntity>
    {
        public HubActivityValidator(ILocalizationService localizationService)
        {
            this.RuleFor(x => x.Type)
                .NotEmpty();
        }
    }
}