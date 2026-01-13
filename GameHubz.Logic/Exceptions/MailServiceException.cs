namespace GameHubz.Logic.Exceptions
{
    public class MailServiceException : BaseException
    {
        public MailServiceException(string message)
            : base(message)
        { }

        public MailServiceException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
