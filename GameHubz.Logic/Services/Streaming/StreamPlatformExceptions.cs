namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Twitch / Kick could not be reached or answered with a temporary failure (429, 5xx, timeout). The
    /// linked account is fine — the caller asks the player to try again, never to re-link.
    /// </summary>
    public class StreamPlatformUnavailableException : Exception
    {
        public StreamPlatformUnavailableException(string message, Exception? inner = null)
            : base(message, inner)
        {
        }
    }

    /// <summary>
    /// The platform refused the access token (401): it was revoked or expired early. The caller refreshes
    /// once; if the refresh is refused too, the account needs a re-link.
    /// </summary>
    public class StreamTokenRejectedException : Exception
    {
        public StreamTokenRejectedException(string message)
            : base(message)
        {
        }
    }
}
