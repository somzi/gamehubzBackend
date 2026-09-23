using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    /// <summary>
    /// One player's verification of a match result: the record that ties together everything the
    /// "Verify Result" flow proves.
    ///
    ///   • the server challenge and the moment it was issued (CreatedOn) — server time, not the phone's
    ///   • the biometric step: the challenge signed with the key the GameHubz app keeps behind
    ///     Face ID / fingerprint, checked here, at <see cref="BiometricVerifiedOn"/> — proof of the key,
    ///     and of the biometric only as far as the app is trusted (see VerificationProof)
    ///   • the device it came from, snapshotted as it was at that moment
    ///   • the recording of the final score, uploaded as ordinary match evidence and linked here
    ///
    /// Only a row that reached <see cref="MatchVerificationStatus.Verified"/> counts. The biometric step
    /// proves who unlocked the phone, not what the score was — the recording and the opponent's
    /// confirmation still carry that — so the value of the row is that all of it is one attempt, bound
    /// together by the challenge, in an order the server enforced.
    /// </summary>
    public class MatchResultVerificationEntity : BaseEntity
    {
        public Guid MatchId { get; set; }

        public MatchEntity? Match { get; set; }

        public Guid UserId { get; set; }

        public UserEntity? User { get; set; }

        /// <summary>The registered device row that answered the challenge.</summary>
        public Guid? UserDeviceId { get; set; }

        public UserDeviceEntity? UserDevice { get; set; }

        // Snapshot of the device at the time of the verification. The device row moves on — the OS and
        // the app update — and the record has to keep saying what was true when it was made.
        public Guid DeviceId { get; set; }

        public string Platform { get; set; } = string.Empty;

        public string? DeviceModel { get; set; }

        public string? OsVersion { get; set; }

        public string? AppVersion { get; set; }

        public bool IsPhysicalDevice { get; set; }

        /// <summary>
        /// When the key that answered the challenge was issued. Snapshotted because the device row only
        /// remembers its current key: a key re-issued just before the attempt (a reinstall, biometrics
        /// re-enrolled on the phone) is worth a look, and a later re-issue must not erase that.
        /// </summary>
        public DateTime? DeviceKeyIssuedOn { get; set; }

        public MatchVerificationStatus Status { get; set; }

        /// <summary>Random, single-use, hex. The device signs it; see VerificationProof.</summary>
        public string Challenge { get; set; } = string.Empty;

        public DateTime ChallengeExpiresOn { get; set; }

        /// <summary>When the server accepted the biometric proof. Null until then.</summary>
        public DateTime? BiometricVerifiedOn { get; set; }

        /// <summary>
        /// The recording, stored as a normal evidence row so it shows in the match gallery and follows
        /// the same retention as every other clip. Nulled if that row is ever hard-deleted; after the
        /// retention sweep retires it the id stays and the navigation simply comes back empty.
        /// </summary>
        public Guid? MatchEvidenceId { get; set; }

        public MatchEvidenceEntity? MatchEvidence { get; set; }

        public DateTime? EvidenceUploadedOn { get; set; }

        /// <summary>
        /// Set while one request is storing this attempt's recording. Taken atomically before the upload
        /// starts, so a retry that lands while the first upload is still running — the phone gave up
        /// waiting, the server did not — cannot store the clip a second time. A claim older than the
        /// stale window counts as abandoned: a request that died mid-upload must not lock the attempt.
        /// </summary>
        public DateTime? EvidenceUploadClaimedOn { get; set; }

        /// <summary>
        /// When the clip says it was recorded, as the phone read it from the file's own metadata. A hint
        /// for the organizer, never proof: metadata is whatever the file claims.
        /// </summary>
        public DateTime? RecordedOn { get; set; }

        public int? EvidenceDurationMs { get; set; }

        public string? EvidenceFileName { get; set; }

        /// <summary>When the row became <see cref="MatchVerificationStatus.Verified"/>.</summary>
        public DateTime? VerifiedOn { get; set; }

        /// <summary>Machine-readable reason for a <see cref="MatchVerificationStatus.Failed"/> row.</summary>
        public string? FailureReason { get; set; }
    }
}
