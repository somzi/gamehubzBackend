using FluentValidation;
using GameHubz.DataModels.Domain;
using GameHubz.Logic.Interfaces;

namespace GameHubz.Logic.Validators
{
    public class TournamentGroupValidator : AbstractValidator<TournamentGroupEntity>
    {
        public TournamentGroupValidator(ILocalizationService localizationService)
        {
            this.RuleFor(x => x.Name)
                .NotEmpty()
;

        }
    }
}