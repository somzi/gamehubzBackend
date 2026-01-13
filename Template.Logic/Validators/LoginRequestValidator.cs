using FluentValidation;

namespace Template.Logic.Validators
{
	public class LoginRequestValidator : AbstractValidator<LoginRequestDto>
	{
		private readonly ILocalizationService localizationService;

		public LoginRequestValidator(ILocalizationService localizationService)
		{
			this.localizationService = localizationService;

			this.RuleFor(x => x.Email)
				.NotEmpty().WithMessage(this.localizationService["LoginRequestValidator.EmailIsEmpty"]);

			this.RuleFor(x => x.Password)
				.NotEmpty().WithMessage(this.localizationService["LoginRequestValidator.PasswordIsEmpty"]);
		}
	}
}