namespace Template.Logic.Exceptions
{
    public class EmptyQueueConfigException : BaseException
    {
        public EmptyQueueConfigException(
            ILocalizationService localizationService)
            : base(localizationService, "Exception.EmptyQueueConfig")
        {
        }
    }
}