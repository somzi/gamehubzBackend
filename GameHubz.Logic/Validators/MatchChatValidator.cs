using FluentValidation;

namespace GameHubz.Logic.Validators
{
    public class MatchChatValidator : AbstractValidator<MatchChatEntity>
    {
        public MatchChatValidator(ILocalizationService localizationService)
        {
            this.RuleFor(x => x.Content)
                .NotEmpty();
        }
    }
}