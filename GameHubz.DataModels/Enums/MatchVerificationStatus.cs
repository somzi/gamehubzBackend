namespace GameHubz.DataModels.Enums
{
    /// <summary>
    /// Where one result verification stands. A verification is a single attempt that has to clear
    /// three steps in order — a server challenge, a biometric proof from a registered device, and the
    /// recording of the final score — and it only counts once all three have landed on the same row.
    /// A clip uploaded on its own is ordinary evidence; it is this row that makes it a verified one.
    /// </summary>
    public enum MatchVerificationStatus
    {
        /// <summary>Challenge issued, nothing proven yet. Expires with the challenge.</summary>
        Started = 0,

        /// <summary>
        /// The device answered the challenge with its biometric-locked key. Waiting for the recording,
        /// which has to arrive inside the evidence window.
        /// </summary>
        BiometricVerified = 1,

        /// <summary>Biometric proof and recording both landed inside their windows. The only state that counts.</summary>
        Verified = 2,

        /// <summary>
        /// The proof did not match the key issued to the device. Kept rather than deleted: an answer
        /// signed with the wrong key is exactly what an organizer wants to be able to see.
        /// </summary>
        Failed = 3,
    }
}
