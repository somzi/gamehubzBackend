using FluentValidation;

namespace GameHubz.Logic.Validators
{
    public class FriendshipValidator : AbstractValidator<FriendshipEntity>
    {
        public FriendshipValidator(ILocalizationService localizationService)
        {
            this.RuleFor(x => x.UserAId).NotEmpty();
            this.RuleFor(x => x.UserBId).NotEmpty();
        }
    }
}
