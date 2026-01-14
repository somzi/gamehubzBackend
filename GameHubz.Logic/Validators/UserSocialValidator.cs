using FluentValidation;
using GameHubz.DataModels.Domain;
using GameHubz.Logic.Interfaces;

namespace GameHubz.Logic.Validators
{
    public class UserSocialValidator : AbstractValidator<UserSocialEntity>
    {
        public UserSocialValidator(ILocalizationService localizationService)
        {
            this.RuleFor(x => x.Type)
                .NotEmpty();
            this.RuleFor(x => x.Username)
                .NotEmpty();
        }
    }
}