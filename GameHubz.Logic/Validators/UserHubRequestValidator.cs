using FluentValidation;

namespace GameHubz.Logic.Validators
{
    public class UserHubRequestValidator : AbstractValidator<UserHubRequestEntity>
    {
        public UserHubRequestValidator()
        {
            RuleFor(x => x.HubId)
                .NotEmpty().WithMessage("HubId is required.");

            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("UserId is required.");
        }
    }
}
