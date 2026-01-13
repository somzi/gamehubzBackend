using FluentValidation;

namespace GameHubz.Logic.Validators
{
    public class TournamentRegistrationValidator : AbstractValidator<TournamentRegistrationEntity>
    {
        public TournamentRegistrationValidator(ILocalizationService localizationService)
        {
        }
    }
}