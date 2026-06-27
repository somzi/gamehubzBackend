using FluentValidation;

namespace GameHubz.Logic.Validators
{
    public class FriendRequestValidator : AbstractValidator<FriendRequestEntity>
    {
        public FriendRequestValidator(ILocalizationService localizationService)
        {
            this.RuleFor(x => x.FromUserId).NotEmpty();
            this.RuleFor(x => x.ToUserId).NotEmpty();
        }
    }
}
