using FluentValidation;

namespace GameHubz.Logic.Validators
{
    public class MatchEvidenceValidator : AbstractValidator<MatchEvidenceEntity>
    {
        public MatchEvidenceValidator(ILocalizationService localizationService)
        {
        }
    }
}