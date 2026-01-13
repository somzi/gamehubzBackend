namespace Template.Logic.Exceptions
{
    public class InvalidDeserializedMessageException : BaseException
    {
        public InvalidDeserializedMessageException(
            ILocalizationService localizationService)
            : base(localizationService, "Exception.InvalidDeserializedMessage")
        {
        }
    }
}