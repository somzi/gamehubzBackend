using FluentValidation;
using GameHubz.DataModels.Domain;
using GameHubz.Logic.Interfaces;

namespace GameHubz.Logic.Validators
{
    public class TournamentStageValidator : AbstractValidator<TournamentStageEntity>
    {
        public TournamentStageValidator(ILocalizationService localizationService)
        {
            this.RuleFor(x => x.Type)
                .NotEmpty()
;
            this.RuleFor(x => x.Order)
                .NotEmpty()
;

        }
    }
}