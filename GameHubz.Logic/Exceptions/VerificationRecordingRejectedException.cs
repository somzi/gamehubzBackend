namespace GameHubz.Logic.Exceptions
{
    /// <summary>
    /// The recording sent to prove a game cannot prove this attempt (reused, or recorded outside the biometric check).
    /// Still a business rule, but surfaced as 409 so the phone can send the player back to choosing a clip
    /// for the same attempt — a retry of the upload would only be refused again.
    /// </summary>
    public class VerificationRecordingRejectedException : BusinessRuleException
    {
        public VerificationRecordingRejectedException(string message)
            : base(message)
        {
        }
    }
}
