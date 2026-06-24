namespace GameHubz.Logic.Exceptions
{
    /// <summary>
    /// Thrown for expected business-rule violations (e.g. acting on an entity whose current
    /// state doesn't allow the operation). The exception middleware maps <see cref="BaseException"/>
    /// to HTTP 400 and does NOT persist it to the ErrorLog table, so these stay out of the
    /// server-fault noise and surface the message to the client unchanged.
    /// </summary>
    public class BusinessRuleException : BaseException
    {
        public BusinessRuleException(string message)
            : base(message)
        {
        }
    }
}
