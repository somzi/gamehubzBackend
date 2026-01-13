using FluentValidation;

namespace GameHubz.Logic.Validators
{
    public class UserHubValidator : AbstractValidator<UserHubEntity>
    {
        public UserHubValidator(ILocalizationService localizationService)
        {
        }
    }
}