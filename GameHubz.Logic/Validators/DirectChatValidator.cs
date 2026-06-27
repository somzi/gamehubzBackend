using FluentValidation;

namespace GameHubz.Logic.Validators
{
    public class DirectChatValidator : AbstractValidator<DirectChatEntity>
    {
        public DirectChatValidator(ILocalizationService localizationService)
        {
            this.RuleFor(x => x.UserAId).NotEmpty();
            this.RuleFor(x => x.UserBId).NotEmpty();
        }
    }

    public class DirectMessageValidator : AbstractValidator<DirectMessageEntity>
    {
        public DirectMessageValidator(ILocalizationService localizationService)
        {
            this.RuleFor(x => x.ChatId).NotEmpty();
            this.RuleFor(x => x.SenderId).NotEmpty();
            this.RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
        }
    }

    public class UserBlockValidator : AbstractValidator<UserBlockEntity>
    {
        public UserBlockValidator(ILocalizationService localizationService)
        {
            this.RuleFor(x => x.BlockerId).NotEmpty();
            this.RuleFor(x => x.BlockedId).NotEmpty();
        }
    }
}
