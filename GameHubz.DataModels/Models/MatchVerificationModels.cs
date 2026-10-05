using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// What the phone reports about itself when it registers for result verification. All of it is
    /// the phone's word: useful to an organizer comparing one verification with the next, never a
    /// security boundary on its own. The key issued in return is what the server actually checks.
    /// </summary>
    public class RegisterVerificationDeviceRequest
    {
        /// <summary>
        /// The installation id the phone already holds, if any. Null on a phone that has never
        /// registered — the server issues one. Sent again after a reinstall-free key loss (biometrics
        /// changed on the phone), which re-issues the key on the same device row.
        /// </summary>
        public Guid? DeviceId { get; set; }

        public string? Platform { get; set; }

        public string? DeviceModel { get; set; }

        public string? DeviceBrand { get; set; }

        public string? OsVersion { get; set; }

        public string? AppVersion { get; set; }

        public bool IsPhysicalDevice { get; set; } = true;

        /// <summary>iOS identifierForVendor / Android ANDROID_ID. Hashed on arrival, never stored raw.</summary>
        public string? PlatformDeviceId { get; set; }

        public DateTime? AppInstalledOn { get; set; }
    }

    public class RegisterVerificationDeviceResponse
    {
        public Guid DeviceId { get; set; }

        /// <summary>
        /// The HMAC key, hex. Returned by this call and never again: the phone locks it behind its
        /// biometrics, and every later verification proves possession by signing a challenge with it.
        /// </summary>
        public string Secret { get; set; } = string.Empty;

        public DateTime KeyIssuedOn { get; set; }
    }

    public class StartMatchVerificationRequest
    {
        /// <summary>
        /// The game this attempt proves. Both numbers are required for new attempts; legacy
        /// whole-match records remain valid but can no longer be created.
        /// </summary>
        public int? SeriesNumber { get; set; }

        public int? GameNumber { get; set; }

        public Guid DeviceId { get; set; }

        // What the phone is now. Registration only happens once per key, so these keep the device row —
        // and the record snapshotted from it — current across OS and app updates. Optional: absent
        // fields keep what is stored.
        public string? DeviceModel { get; set; }

        public string? OsVersion { get; set; }

        public string? AppVersion { get; set; }
    }

    public class StartMatchVerificationResponse
    {
        public Guid VerificationId { get; set; }

        /// <summary>Random, single-use, hex. Signed by the phone together with the ids of this attempt.</summary>
        public string Challenge { get; set; } = string.Empty;

        public DateTime ExpiresOn { get; set; }

        /// <summary>
        /// The exact string the phone has to sign, so the client never has to rebuild it — see
        /// VerificationProof.BuildMessage for the format and why every id is in it.
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// What a finished recording upload writes onto its verification, in one conditional update — see
    /// IMatchResultVerificationRepository.TryCompleteEvidenceUpload.
    /// </summary>
    public record VerificationUploadResult(
        Guid EvidenceId,
        DateTime CompletedOn,
        DateTime? RecordedOn,
        int? DurationMs,
        string? FileName,
        Guid CompletedBy,
        DateTime? RawRecordedOn = null);

    public class SubmitBiometricProofRequest
    {
        /// <summary>HMAC-SHA256 of the start response's Message under the device key, hex.</summary>
        public string Signature { get; set; } = string.Empty;
    }

    /// <summary>Optional facts about the recording, read on the phone and sent with the file.</summary>
    public class AttachVerificationEvidenceRequest
    {
        public string? Signature { get; set; }
        public long? ClockOffsetMs { get; set; }
        public int? TimeZoneOffsetMinutes { get; set; }

        public int? DurationMs { get; set; }

        /// <summary>The clip's own creation time from its MP4 metadata, when the phone could read one.</summary>
        public DateTime? RecordedOn { get; set; }

        public string? FileName { get; set; }
    }

    /// <summary>
    /// Everything the match screen needs to render verification: whether it is required, whether the
    /// caller can (and still has to) verify, and the records to show. Caller-relative on purpose — an
    /// organizer gets every record with its device details, a player gets the two sides' verified
    /// state and their own device, a spectator gets the verified state only.
    /// </summary>
    public class MatchVerificationPanelDto
    {
        public Guid MatchId { get; set; }

        /// <summary>
        /// Every game of the match's format, main series first, with each player's proof of it. A
        /// whole-match proof fills every game of its player.
        /// </summary>
        public List<MatchVerificationGameDto> Games { get; set; } = new();

        /// <summary>The tournament setting.</summary>
        public bool Required { get; set; }

        /// <summary>
        /// The caller plays this match, it is still open for a result, and an app from before per-game
        /// verification could still need its one, whole-match verification: there is none yet, and outside
        /// a knockout their proofs do not yet cover every game the match can still be reported with (in a
        /// knockout a tiebreak may always follow). The current app reads <see cref="MatchVerificationGameDto.CanVerify"/>.
        /// </summary>
        public bool CanVerify { get; set; }

        /// <summary>
        /// The server would refuse the caller's next report for want of a verification, judged on the
        /// games already on file — or, before any score exists, on the shortest series still to be played
        /// (see VerificationGames). The app checks the score being typed against <see cref="Games"/>
        /// instead; an app from before per-game verification follows this flag.
        /// </summary>
        public bool ReportBlocked { get; set; }

        /// <summary>The caller manages the tournament — the organizer view with device details.</summary>
        public bool IsManager { get; set; }

        /// <summary>
        /// The caller plays this match. An organizer who does reports it as a player, with proofs; only
        /// an organizer outside the match is exempt from the gate.
        /// </summary>
        public bool IsParticipant { get; set; }

        public bool PhoneApprovalPending { get; set; }

        /// <summary>
        /// The caller's proof as an app from before per-game verification sees it: the first game's, once
        /// that app needs nothing more (see <see cref="CanVerify"/>; a whole-match proof always settles it);
        /// null before, so that app still offers its verification.
        /// </summary>
        public MatchVerificationRecordDto? Mine { get; set; }

        /// <summary>The first game's <see cref="MatchVerificationGameDto.Records"/>.</summary>
        public List<MatchVerificationRecordDto> Records { get; set; } = new();
    }

    public class MatchVerificationGameDto
    {
        public int SeriesNumber { get; set; } = 1;

        public int GameNumber { get; set; } = 1;

        /// <summary>The caller can still prove this game.</summary>
        public bool CanVerify { get; set; }

        /// <summary>The caller's proof of this game, if any.</summary>
        public MatchVerificationRecordDto? Mine { get; set; }

        /// <summary>
        /// One entry per player of the match: their latest proof of this game, or a placeholder with
        /// <see cref="MatchVerificationRecordDto.Status"/> Started and no timestamps when they have not
        /// verified it. Organizers additionally get the failed attempts at it, newest first, after the players.
        /// </summary>
        public List<MatchVerificationRecordDto> Records { get; set; } = new();
    }

    public class MatchVerificationRecordDto
    {
        /// <summary>The proven game; 0 for both on a whole-match proof (see MatchResultVerificationEntity.WholeMatch).</summary>
        public int SeriesNumber { get; set; } = 1;

        public int GameNumber { get; set; } = 1;

        /// <summary>Null for a "has not verified yet" placeholder.</summary>
        public Guid? Id { get; set; }

        public Guid UserId { get; set; }

        public string Username { get; set; } = string.Empty;

        public string? AvatarUrl { get; set; }

        public MatchVerificationStatus Status { get; set; }

        public bool BiometricVerified { get; set; }

        // Only returned to the owner by the biometric endpoint, never on public panel records.
        public string? EvidenceMessage { get; set; }

        /// <summary>Server time the challenge was issued.</summary>
        public DateTime? StartedOn { get; set; }

        public DateTime? BiometricVerifiedOn { get; set; }

        public DateTime? VerifiedOn { get; set; }

        /// <summary>The recording. Null when none was attached, or after retention retired it.</summary>
        public MatchEvidenceItemDto? Evidence { get; set; }

        /// <summary>A recording was attached but has since been removed by the retention sweep.</summary>
        public bool EvidenceExpired { get; set; }

        public int? EvidenceDurationMs { get; set; }

        public DateTime? RecordedOn { get; set; }

        public string? FailureReason { get; set; }

        /// <summary>Device details — the organizer's view, and the player's own record. Null otherwise.</summary>
        public MatchVerificationDeviceDto? Device { get; set; }

        /// <summary>
        /// Things an organizer should look at, as codes the client localizes: "newDevice",
        /// "freshKey", "sharedDevice", "emulator", "oldRecording". Organizer view only.
        /// </summary>
        public List<string> Flags { get; set; } = new();
    }

    public class MatchVerificationDeviceDto
    {
        public Guid DeviceId { get; set; }

        public string Platform { get; set; } = string.Empty;

        public string? DeviceModel { get; set; }

        public string? OsVersion { get; set; }

        public string? AppVersion { get; set; }

        public bool IsPhysicalDevice { get; set; }

        /// <summary>When the account first registered this phone.</summary>
        public DateTime? FirstSeenOn { get; set; }

        public DateTime? KeyIssuedOn { get; set; }

        /// <summary>Other accounts registered on the same phone (same installation or same platform id).</summary>
        public int OtherAccountsOnDevice { get; set; }

        /// <summary>
        /// Those accounts by name — "possibly played by Žika" is actionable where "shared phone" is not.
        /// Organizer view only; empty for anyone else, the player's own record included.
        /// </summary>
        public List<MatchVerificationDeviceAccountDto> OtherAccounts { get; set; } = new();

        /// <summary>How many phones this account has registered in total.</summary>
        public int AccountDeviceCount { get; set; }
    }

    /// <summary>Another account registered on the phone a verification came from.</summary>
    public class MatchVerificationDeviceAccountDto
    {
        public Guid UserId { get; set; }

        public string Username { get; set; } = string.Empty;

        public string? AvatarUrl { get; set; }

        /// <summary>When that account first registered on this phone.</summary>
        public DateTime? FirstSeenOn { get; set; }

        /// <summary>
        /// It was on this phone before the account that verified — so the phone is more likely
        /// theirs, and so, possibly, is the hand that played.
        /// </summary>
        public bool CameFirst { get; set; }
    }
}
