namespace GameHubz.Logic.Exceptions
{
    public class InvalidTokenException : BaseException
    {
        public InvalidTokenException()
            : this(Strings.InvalidTokenException)
        {
        }

        public InvalidTokenException(string message)
            : base(message)
        {
        }

        public InvalidTokenException(Exception innerException)
            : this(Strings.InvalidTokenException, innerException)
        {
        }

        public InvalidTokenException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
