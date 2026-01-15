using FluentValidation;

namespace GameHubz.Logic.Validators
{
    public class MatchValidator : AbstractValidator<MatchEntity>
    {
        public MatchValidator(ILocalizationService localizationService)
        {
            this.RuleFor(x => x.TournamentId)
                .NotEmpty();
            this.RuleFor(x => x.HomeParticipantId)
                .NotEmpty();
            this.RuleFor(x => x.AwayParticipantId)
                .NotEmpty();
        }
    }
}