using FluentValidation;

namespace GameHubz.Logic.Validators
{
    public class MatchValidator : AbstractValidator<MatchEntity>
    {
        public MatchValidator(ILocalizationService localizationService)
        {
            this.RuleFor(x => x.TournamentId)
                .NotEmpty();
            this.RuleFor(x => x.HomeUserId)
                .NotEmpty();
            this.RuleFor(x => x.AwayUserId)
                .NotEmpty();
        }
    }
}