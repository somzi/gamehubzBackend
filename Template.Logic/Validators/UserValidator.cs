using FluentValidation;

namespace Template.Logic.Validators
{
    public class UserValidator : AbstractValidator<UserEntity>
    {
        private readonly IAppUnitOfWork unitOfWork;
        private readonly ILocalizationService localizationService;

        public UserValidator(IUnitOfWorkFactory factory, ILocalizationService localizationService)
        {
            this.unitOfWork = factory.CreateAppUnitOfWork();

            this.localizationService = localizationService;

            this.RuleFor(x => x.Email)
                .NotEmpty().WithMessage(this.localizationService["UserValidator.EmailIsEmpty"])
                .Must(this.IsEmailUnique).WithMessage(this.localizationService["Exception.EmailAlreadyInUseException"]);

            this.RuleFor(x => x.Password)
                .NotEmpty()
                .When(x => x.IsNativeAuthentication)
                .WithMessage(this.localizationService["UserValidator.PasswordIsEmpty"]);

            this.RuleFor(x => x.PasswordNonce)
                .MaximumLength(16);

            this.RuleFor(x => x.UserRoleId)
                .NotEmpty().WithMessage(this.localizationService["UserValidator.RoleIsEmpty"]);

            this.RuleFor(x => x.ObjectId)
                .MaximumLength(100)
                    .When(x => x.ObjectId != null)
                .Must(this.IsObjectIdUnique)
                    .When(x => x.ObjectId != null);
        }

        private bool IsEmailUnique(UserEntity user, string email)
        {
            return this.unitOfWork.UserRepository.IsEmailUnique(user, email);
        }

        private bool IsObjectIdUnique(UserEntity user, string? objectId)
        {
            return this.unitOfWork.UserRepository.IsObjectIdUnique(user, objectId);
        }
    }
}