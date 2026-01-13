using FluentValidation;

namespace GameHubz.Logic.Validators
{
    public class AssetValidator : AbstractValidator<AssetEntity>
    {
        public AssetValidator()
        {
            this.RuleFor(x => x.FileName)
                .MaximumLength(256);

            this.RuleFor(x => x.BlobName)
                .MaximumLength(256);

            this.RuleFor(x => x.Extension)
                .MaximumLength(50);

            this.RuleFor(x => x.FileFormat)
                .MaximumLength(50);

            this.RuleFor(x => x.AssetType)
                .NotNull();

            this.RuleFor(x => x.Size)
                .NotEmpty();

            this.RuleFor(x => x.Description)
                .MaximumLength(1000);
        }
    }
}
