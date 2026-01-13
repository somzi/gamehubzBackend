using FluentValidation;

namespace GameHubz.Logic.Validators
{
    public class TournamentValidator : AbstractValidator<TournamentEntity>
    {
        public TournamentValidator(ILocalizationService localizationService)
        {
            this.RuleFor(x => x.Name)
                .NotEmpty()
                .MaximumLength(200);
        }
    }
}