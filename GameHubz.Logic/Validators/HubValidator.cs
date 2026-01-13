using FluentValidation;

namespace GameHubz.Logic.Validators
{
    public class HubValidator : AbstractValidator<HubEntity>
    {
        public HubValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name is required.")
                .MaximumLength(256).WithMessage("Name must be at most 256 characters.");

            RuleFor(x => x.Description)
                .MaximumLength(1000).WithMessage("Description must be at most 1000 characters.");

            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("UserId is required.");
        }
    }
}