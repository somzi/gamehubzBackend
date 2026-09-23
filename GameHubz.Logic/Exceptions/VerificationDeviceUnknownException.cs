namespace GameHubz.Logic.Exceptions
{
    /// <summary>
    /// The phone asked to verify with an installation id this account has no key for — its local
    /// registration outlived the server's record of it (a restored database, a different backend).
    /// Still a business rule, but the one the client can fix on its own by registering again, so the
    /// verification endpoints surface it as 409 instead of the generic 400 the phone cannot tell apart
    /// from "this tournament does not verify".
    /// </summary>
    public class VerificationDeviceUnknownException : BusinessRuleException
    {
        public VerificationDeviceUnknownException(string message)
            : base(message)
        {
        }
    }
}
