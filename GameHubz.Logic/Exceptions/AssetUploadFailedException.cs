namespace GameHubz.Logic.Exceptions
{
    public class AssetUploadFailedException : BaseException
    {
        public AssetUploadFailedException()
            : base()
        {
        }

        public AssetUploadFailedException(ILocalizationService localizationService)
            : this(localizationService["Exception.AssetUploadFailedException"])
        {
        }

        public AssetUploadFailedException(string message)
            : base(message)
        {
        }

        public AssetUploadFailedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
