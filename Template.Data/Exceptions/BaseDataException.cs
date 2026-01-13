namespace Template.Data.Exceptions
{
    public class BaseDataException : Exception
    {
        public BaseDataException(string? message) : base(message)
        {
        }
    }
}