namespace GameHubz.Logic.Interfaces
{
    public interface IMatchResultVerificationRepository : IRepository<MatchResultVerificationEntity>
    {
        /// <summary>
        /// Whether this user holds a completed verification of this game — or of the whole match, which
        /// covers every game. Asked with <see cref="MatchResultVerificationEntity.WholeMatch"/>, only a
        /// whole-match verification answers.
        /// </summary>
        Task<bool> HasVerified(Guid matchId, Guid userId, int seriesNumber, int gameNumber);

        /// <summary>
        /// Whether this user has proven every one of these games — the question the result gate asks. A
        /// verification from before a rejected proposal still counts: it proves the same player unlocked
        /// the same phone and recorded that game of the same match. Nothing to prove is true.
        /// </summary>
        Task<bool> HasVerifiedGames(Guid matchId, Guid userId, IReadOnlyCollection<(int Series, int Game)> games);

        /// <summary>
        /// Whether this user already proved another game of this match with the same recording: one
        /// recorded at the same moment and running exactly as long, as the phone read them from the clip.
        /// False whenever either fact is missing.
        /// </summary>
        Task<bool> IsRecordingUsedForAnotherGame(
            Guid matchId,
            Guid userId,
            Guid verificationId,
            DateTime? recordedOn,
            int? durationMs);

        /// <summary>Whether anyone holds a completed verification on any of these matches.</summary>
        Task<bool> AnyVerifiedForMatches(IReadOnlyCollection<Guid> matchIds);

        /// <summary>Tracked, with its device row, for the steps that advance an attempt.</summary>
        Task<MatchResultVerificationEntity?> GetForUpdate(Guid verificationId);

        /// <summary>Every attempt on the match that is worth showing — verified and failed — with user, device and clip.</summary>
        Task<List<MatchResultVerificationEntity>> GetShownForMatch(Guid matchId);

        /// <summary>Attempts this user started on this match since a moment — the rate limit.</summary>
        Task<int> CountStartedSince(Guid matchId, Guid userId, DateTime since);

        /// <summary>
        /// Atomically claims the recording upload of an attempt that is waiting for one. False when the
        /// attempt is not waiting any more, or another request holds a claim newer than
        /// <paramref name="staleBefore"/>. Decided in the database, so two requests cannot both win.
        /// </summary>
        Task<bool> TryClaimEvidenceUpload(Guid verificationId, DateTime claimedOn, DateTime staleBefore);

        /// <summary>Gives a claim back after a failed upload, so the player's retry can take it at once.</summary>
        Task ReleaseEvidenceUploadClaim(Guid verificationId, DateTime claimedOn);

        /// <summary>
        /// Completes the attempt — status Verified, the clip linked — but only while the claim taken at
        /// <paramref name="claimedOn"/> is still the one on the row. False when another request took the
        /// claim over (and possibly finished) while this one was uploading.
        /// </summary>
        Task<bool> TryCompleteEvidenceUpload(Guid verificationId, DateTime claimedOn, VerificationUploadResult result);
    }
}
