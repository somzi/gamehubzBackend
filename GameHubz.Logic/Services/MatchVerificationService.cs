using GameHubz.DataModels.Enums;
using Microsoft.AspNetCore.Http;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// "Verify Result": a player proves the final score of a match with three things bound into one
    /// record — a biometric unlock on a phone registered to their account, a screen recording of the
    /// final score, and server timestamps for both.
    ///
    /// The flow, in the order the server enforces it:
    ///   1. <see cref="RegisterDevice"/> — once per phone per account. Issues an HMAC key the phone locks
    ///      behind its biometrics (see <see cref="VerificationProof"/>).
    ///   2. <see cref="Start"/> — a single-use challenge for one match, valid for a few minutes.
    ///   3. <see cref="SubmitBiometricProof"/> — the phone unlocks the key with Face ID / fingerprint and
    ///      signs the challenge; the server checks the signature against the key it issued.
    ///   4. <see cref="AttachEvidence"/> — the recording, inside a window that opens at step 3. It is
    ///      stored as ordinary match evidence and linked, and only now does the record count.
    ///
    /// Biometrics prove who unlocked the phone, never what the score was. The recording and the
    /// opponent's confirmation still decide that — what this adds is accountability: which account, on
    /// which registered installation, attested to the result, and when. Face and fingerprint data never
    /// leave the phone.
    ///
    /// What the server can vouch for is possession of the installation's key, not the biometric check
    /// itself: the GameHubz app only uses the key after the phone's own Face ID / fingerprint check, but
    /// someone signed in to the account could register an "installation" from a script and sign without
    /// one. Closing that takes platform attestation — see <see cref="VerificationProof"/>.
    /// </summary>
    public class MatchVerificationService : AppBaseService
    {
        /// <summary>How long a challenge can be answered. Long enough for a Face ID retry, short enough to be fresh.</summary>
        internal static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

        /// <summary>
        /// How long after the biometric proof the recording may arrive. It covers picking, compressing
        /// and uploading a clip on a slow connection — and a retry — but keeps the two steps one sitting.
        /// </summary>
        internal static readonly TimeSpan EvidenceWindow = TimeSpan.FromMinutes(30);

        /// <summary>
        /// How old an upload claim has to be before another request may take it over. The clip has fully
        /// arrived before the upload starts (the request body is read first), so what the claim covers is
        /// the hop to storage — seconds. Minutes of margin, and a request that died mid-upload still
        /// cannot lock the attempt for longer than this.
        /// </summary>
        internal static readonly TimeSpan UploadClaimStaleAfter = TimeSpan.FromMinutes(5);

        // Organizer flags. Thresholds rather than verdicts: each one is a reason to look, not a ruling.
        internal static readonly TimeSpan NewDeviceAge = TimeSpan.FromHours(24);
        internal static readonly TimeSpan FreshKeyAge = TimeSpan.FromHours(1);
        internal static readonly TimeSpan OldRecordingAge = TimeSpan.FromHours(24);

        public const string FlagNewDevice = "newDevice";
        public const string FlagFreshKey = "freshKey";
        public const string FlagSharedDevice = "sharedDevice";
        public const string FlagEmulator = "emulator";
        public const string FlagOldRecording = "oldRecording";

        public const string FailureSignatureMismatch = "signatureMismatch";

        // Abuse limits. A verification costs a key, a challenge row and possibly a stored clip, so none
        // of them can be minted in a loop.
        private const int MaxKeysIssuedPerDay = 10;
        private const int MaxAttemptsPerMatchPerHour = 10;

        /// <summary>
        /// One verification per player per match. A second one would stand in front of the first — the
        /// panel shows a player's latest — so the recording an organizer sees would be whichever the
        /// player liked better. A wrong clip is the organizer's call to settle, not the player's to replace.
        /// </summary>
        private const int MaxVerifiedPerMatch = 1;

        private static readonly TimeSpan KeyIssueWindow = TimeSpan.FromDays(1);

        private readonly IStorageService storageService;
        private readonly TournamentAuthorizationService tournamentAuth;
        private readonly ICacheService cacheService;

        public MatchVerificationService(
            IUnitOfWorkFactory factory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            IStorageService storageService,
            TournamentAuthorizationService tournamentAuth,
            ICacheService cacheService)
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.storageService = storageService;
            this.tournamentAuth = tournamentAuth;
            this.cacheService = cacheService;
        }

        // Counts issuances, not device rows: re-issuing the key of one phone rewrites the same row, so a
        // row count let a single phone rotate its key without any limit at all.
        private static string KeyIssueCounterKey(Guid userId) => $"verification:keys-issued:{userId}";

        // ─────────────────────────────────────────────────────────────────────
        //  1. Device registration
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Registers the caller's phone, or re-issues its key. Re-issuing on the same row is the recovery
        /// path for a key the phone lost — iOS drops a biometric-locked item when the enrolled faces or
        /// fingers change, Android invalidates the Keystore key the same way — so the device keeps its
        /// history and simply gets a new key. The organizer view shows how recent that key is.
        /// </summary>
        public async Task<RegisterVerificationDeviceResponse> RegisterDevice(RegisterVerificationDeviceRequest request)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            string platform = NormalizePlatform(request.Platform)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationUnsupportedPlatform"]);

            DateTime now = DateTime.UtcNow;

            if (!await this.TryReserveKeyIssue(user.UserId))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationTooManyDevices"]);

            UserDeviceEntity? device = request.DeviceId is Guid requested && requested != Guid.Empty
                ? await this.AppUnitOfWork.UserDeviceRepository.GetForUser(user.UserId, requested)
                : null;

            // A phone that already holds an installation id keeps it, even when this account has never
            // registered on it: the same id under two accounts is exactly how a shared phone shows up.
            Guid deviceId = request.DeviceId is Guid known && known != Guid.Empty ? known : Guid.NewGuid();
            string secret = VerificationProof.NewSecret();

            bool isNew = device == null;
            device ??= new UserDeviceEntity { UserId = user.UserId, DeviceId = deviceId };

            device.Platform = platform;
            device.DeviceModel = Truncate(request.DeviceModel, 128);
            device.DeviceBrand = Truncate(request.DeviceBrand, 64);
            device.OsVersion = Truncate(request.OsVersion, 64);
            device.AppVersion = Truncate(request.AppVersion, 32);
            device.IsPhysicalDevice = request.IsPhysicalDevice;
            device.PlatformDeviceIdHash = VerificationProof.HashPlatformDeviceId(request.PlatformDeviceId) ?? device.PlatformDeviceIdHash;
            device.AppInstalledOn = SanitizePastTimestamp(request.AppInstalledOn, now) ?? device.AppInstalledOn;
            device.KeySecret = secret;
            device.KeyIssuedOn = now;
            device.LastSeenOn = now;

            if (isNew)
                await this.AppUnitOfWork.UserDeviceRepository.AddEntity(device, this.UserContextReader);
            else
                await this.AppUnitOfWork.UserDeviceRepository.UpdateEntity(device, this.UserContextReader);

            await this.SaveAsync();

            return new RegisterVerificationDeviceResponse
            {
                DeviceId = device.DeviceId,
                Secret = secret,
                KeyIssuedOn = now,
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        //  2. Challenge
        // ─────────────────────────────────────────────────────────────────────

        public async Task<StartMatchVerificationResponse> Start(Guid matchId, StartMatchVerificationRequest request)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

            var settings = await this.AppUnitOfWork.TournamentRepository.GetApprovalContext(match.TournamentId)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.TournamentNotFound"]);

            if (!settings.RequireResultVerification)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationNotEnabled"]);

            // Who may verify is decided by the same rule that decides who may report, so the two can
            // never disagree: on a team game that is the player nominated for it, as with check-in.
            if (!BracketService.IsMatchParticipant(match, user.UserId))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.NotAMatchParticipant"]);

            ThrowIfMatchClosed(match);

            // Before the device is even looked up: a phone whose key the server no longer knows would
            // otherwise re-register — spending one of the day's key issuances — only to be told this.
            if (await this.AppUnitOfWork.MatchResultVerificationRepository.CountVerified(matchId, user.UserId) >= MaxVerifiedPerMatch)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationAlreadyVerified"]);

            var device = await this.AppUnitOfWork.UserDeviceRepository.GetForUser(user.UserId, request.DeviceId)
                ?? throw new VerificationDeviceUnknownException(this.LocalizationService["BusinessRule.VerificationDeviceNotRegistered"]);

            DateTime now = DateTime.UtcNow;

            if (await this.AppUnitOfWork.MatchResultVerificationRepository.CountStartedSince(matchId, user.UserId, now.AddHours(-1)) >= MaxAttemptsPerMatchPerHour)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationTooManyAttempts"]);

            // The phone says what it is now, on every attempt. The row was written at registration and
            // is not registered again while its key still works, so without this every later record
            // would carry the OS and app version the phone had on its first day. Description only — the
            // key is untouched. Absent fields (an older client) keep what is stored.
            if (!string.IsNullOrWhiteSpace(request.DeviceModel)) device.DeviceModel = Truncate(request.DeviceModel, 128);
            if (!string.IsNullOrWhiteSpace(request.OsVersion)) device.OsVersion = Truncate(request.OsVersion, 64);
            if (!string.IsNullOrWhiteSpace(request.AppVersion)) device.AppVersion = Truncate(request.AppVersion, 32);

            var verification = new MatchResultVerificationEntity
            {
                MatchId = matchId,
                UserId = user.UserId,
                UserDeviceId = device.Id,
                DeviceId = device.DeviceId,
                Platform = device.Platform,
                DeviceModel = device.DeviceModel,
                OsVersion = device.OsVersion,
                AppVersion = device.AppVersion,
                IsPhysicalDevice = device.IsPhysicalDevice,
                DeviceKeyIssuedOn = device.KeyIssuedOn,
                Status = MatchVerificationStatus.Started,
                Challenge = VerificationProof.NewChallenge(),
                ChallengeExpiresOn = now.Add(ChallengeLifetime),
            };

            await this.AppUnitOfWork.MatchResultVerificationRepository.AddEntity(verification, this.UserContextReader);

            device.LastSeenOn = now;
            await this.AppUnitOfWork.UserDeviceRepository.UpdateEntity(device, this.UserContextReader);

            await this.SaveAsync();

            return new StartMatchVerificationResponse
            {
                VerificationId = verification.Id!.Value,
                Challenge = verification.Challenge,
                ExpiresOn = verification.ChallengeExpiresOn,
                Message = VerificationProof.BuildMessage(
                    verification.Id.Value, matchId, user.UserId, device.DeviceId, verification.Challenge),
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        //  3. Biometric proof
        // ─────────────────────────────────────────────────────────────────────

        public async Task<MatchVerificationRecordDto> SubmitBiometricProof(Guid verificationId, SubmitBiometricProofRequest request)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var verification = await this.LoadOwnVerification(verificationId, user.UserId);

            // A retry whose first answer was lost on the way back: the proof is already in, and saying
            // so is the idempotent answer. Anything else past this point is a fresh attempt at step 3.
            if (verification.Status is MatchVerificationStatus.BiometricVerified or MatchVerificationStatus.Verified)
                return await this.BuildOwnRecord(verification);

            if (verification.Status == MatchVerificationStatus.Failed)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationAttemptFailed"]);

            DateTime now = DateTime.UtcNow;

            if (now > verification.ChallengeExpiresOn)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationExpired"]);

            var device = verification.UserDeviceId.HasValue
                ? await this.AppUnitOfWork.UserDeviceRepository.GetById(verification.UserDeviceId.Value)
                : null;

            if (device == null)
                throw new VerificationDeviceUnknownException(this.LocalizationService["BusinessRule.VerificationDeviceNotRegistered"]);

            string message = VerificationProof.BuildMessage(
                verification.Id!.Value, verification.MatchId, verification.UserId, verification.DeviceId, verification.Challenge);

            // The key this proof is checked against, not the one the device had when the challenge was
            // issued. The phone re-registers between the two exactly when the OS dropped its locked key —
            // faces or fingers changed on the phone — and that fresh key is the one worth flagging.
            verification.DeviceKeyIssuedOn = device.KeyIssuedOn;

            if (!VerificationProof.IsValid(device.KeySecret, message, request.Signature))
            {
                // Recorded, then refused. The failed attempt is the signal an organizer needs — an answer
                // signed with a key this phone was never given — so it is saved before the error goes out.
                verification.Status = MatchVerificationStatus.Failed;
                verification.FailureReason = FailureSignatureMismatch;
                await this.AppUnitOfWork.MatchResultVerificationRepository.UpdateEntity(verification, this.UserContextReader);
                await this.SaveAsync();

                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationProofRejected"]);
            }

            verification.Status = MatchVerificationStatus.BiometricVerified;
            verification.BiometricVerifiedOn = now;
            await this.AppUnitOfWork.MatchResultVerificationRepository.UpdateEntity(verification, this.UserContextReader);

            device.LastSeenOn = now;
            await this.AppUnitOfWork.UserDeviceRepository.UpdateEntity(device, this.UserContextReader);

            await this.SaveAsync();

            return await this.BuildOwnRecord(verification);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  4. Recording
        // ─────────────────────────────────────────────────────────────────────

        public async Task<MatchVerificationRecordDto> AttachEvidence(
            Guid verificationId,
            IFormFile? file,
            AttachVerificationEvidenceRequest meta)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var verification = await this.LoadOwnVerification(verificationId, user.UserId);

            // The upload landed but its answer did not: a retry must not store the clip twice.
            if (verification.Status == MatchVerificationStatus.Verified)
                return await this.BuildOwnRecord(verification);

            if (verification.Status != MatchVerificationStatus.BiometricVerified || verification.BiometricVerifiedOn == null)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationBiometricFirst"]);

            DateTime now = DateTime.UtcNow;

            if (now > verification.BiometricVerifiedOn.Value.Add(EvidenceWindow))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationExpired"]);

            if (file == null || file.Length == 0)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationRecordingRequired"]);

            // A recording of the final score is a clip. A screenshot is still welcome as ordinary
            // evidence, but it cannot complete a verification.
            if (!MatchService.AllowedVideoTypes.Contains(file.ContentType ?? string.Empty))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationRecordingMustBeVideo"]);

            var match = await this.AppUnitOfWork.MatchRepository.ShallowGetById(verification.MatchId)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

            // Settled while the player was picking the clip: there is no longer a report to verify.
            ThrowIfMatchClosed(match);

            // A second attempt the player kept open while another one finished. Start refuses a new
            // attempt once the match has their verification; this is the same rule for one that was
            // already under way. (This attempt being the verified one returned above.)
            if (await this.AppUnitOfWork.MatchResultVerificationRepository.CountVerified(verification.MatchId, user.UserId) >= MaxVerifiedPerMatch)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationAlreadyVerified"]);

            // One upload per attempt. The Verified check above only covers a retry that arrives after the
            // first upload finished; a retry that arrives DURING it — the phone gave up waiting at its
            // timeout, the server carried on — would pass the same check and store the clip again. The
            // claim is taken in the database, so only one of two concurrent requests can hold it.
            //
            // Whole milliseconds: the release below finds its own claim by equality, and Postgres keeps
            // microseconds where .NET keeps 100ns ticks — an untrimmed stamp would never match on the way back.
            DateTime claimedOn = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);

            if (!await this.AppUnitOfWork.MatchResultVerificationRepository.TryClaimEvidenceUpload(
                    verificationId, claimedOn, now - UploadClaimStaleAfter))
            {
                var current = await this.AppUnitOfWork.MatchResultVerificationRepository.GetForUpdate(verificationId);
                if (current?.Status == MatchVerificationStatus.Verified)
                    return await this.BuildOwnRecord(current);

                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationUploadInProgress"]);
            }

            StoredAsset? stored = null;
            MatchEvidenceEntity? evidence = null;

            // Same all-or-nothing rule as ordinary evidence: an uploaded clip with no row pointing at it is
            // invisible to the retention sweep and would be paid for forever, so a failed save takes it back.
            // Any failure also hands the claim back, so the player's retry does not wait out the stale window.
            try
            {
                var folder = await this.AppUnitOfWork.MatchRepository.GetForMatchEvidence(verification.MatchId);
                string folderPath = $"hub/{folder.HubName}/tournaments/{folder.TournamentName}/matches/{verification.MatchId}";
                string fileName = $"verification_{verification.MatchId}_{DateTime.UtcNow.Ticks}";

                stored = await this.storageService.UploadVideoAsync(file, folderPath, fileName)
                    ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationUploadFailed"]);

                // When the attempt actually completes — after the upload, not before it.
                DateTime completedOn = DateTime.UtcNow;
                var uploaded = stored;

                // The evidence row and the completion commit together, and the completion only happens while
                // this request still holds the claim. A request that sat in the upload past the stale window
                // may have been overtaken — the takeover stored its own clip and completed the attempt — so a
                // plain save here would overwrite a finished record with a second clip. Losing the claim
                // instead rolls this request's row back; its clip is deleted below.
                await this.AppUnitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    evidence = new MatchEvidenceEntity
                    {
                        MatchId = verification.MatchId,
                        Url = uploaded.Url,
                        StorageKey = uploaded.StorageKey,
                        Provider = uploaded.Provider,
                        MediaType = EvidenceMediaType.Video,
                    };

                    await this.AppUnitOfWork.MatchEvidenceRepository.AddEntity(evidence, this.UserContextReader);
                    await this.SaveAsync();

                    bool completed = await this.AppUnitOfWork.MatchResultVerificationRepository.TryCompleteEvidenceUpload(
                        verificationId,
                        claimedOn,
                        new VerificationUploadResult(
                            evidence.Id!.Value,
                            completedOn,
                            SanitizePastTimestamp(meta.RecordedOn, now),
                            meta.DurationMs is > 0 and < 60 * 60 * 1000 ? meta.DurationMs : null,
                            Truncate(meta.FileName, 256),
                            user.UserId));

                    if (!completed) throw new UploadClaimLostException();

                    if (verification.UserDeviceId.HasValue)
                    {
                        var device = await this.AppUnitOfWork.UserDeviceRepository.GetById(verification.UserDeviceId.Value);
                        if (device != null)
                        {
                            device.LastSeenOn = completedOn;
                            device.LastVerifiedOn = completedOn;
                            await this.AppUnitOfWork.UserDeviceRepository.UpdateEntity(device, this.UserContextReader);
                            await this.SaveAsync();
                        }
                    }

                    return true;
                });
            }
            catch (UploadClaimLostException)
            {
                // Overtaken: nothing of this request stays — the row rolled back, the clip goes now. The
                // attempt itself is whatever the takeover made of it.
                if (evidence != null) await this.AppUnitOfWork.MatchEvidenceRepository.DetachEntity(evidence);
                await this.DeleteStoredVideo(stored);

                var current = await this.AppUnitOfWork.MatchResultVerificationRepository.GetForUpdate(verificationId);
                if (current?.Status == MatchVerificationStatus.Verified)
                    return await this.BuildOwnRecord(current);

                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationUploadInProgress"]);
            }
            catch
            {
                await this.DeleteStoredVideo(stored);

                try { await this.AppUnitOfWork.MatchResultVerificationRepository.ReleaseEvidenceUploadClaim(verificationId, claimedOn); }
                catch { /* worst case the claim goes stale on its own */ }

                throw;
            }

            // Read back rather than patched in memory: the completion was written straight to the database.
            var verified = await this.AppUnitOfWork.MatchResultVerificationRepository.GetForUpdate(verificationId) ?? verification;
            return await this.BuildOwnRecord(verified);
        }

        /// <summary>This request no longer holds the upload claim — another one took it over and finished.</summary>
        private sealed class UploadClaimLostException : Exception
        {
        }

        private async Task DeleteStoredVideo(StoredAsset? stored)
        {
            if (stored == null) return;

            try { await this.storageService.DeleteAsync(stored.StorageKey, EvidenceMediaType.Video); }
            catch { /* the failure the caller needs to see is the one already in flight */ }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Read
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The verification state of a match, shaped for whoever is asking — see
        /// <see cref="MatchVerificationPanelDto"/>. Organizers get every device detail and the flags;
        /// players get both sides' state and their own device; anyone else only the verified state.
        /// </summary>
        public async Task<MatchVerificationPanelDto> GetPanel(Guid matchId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

            var settings = await this.AppUnitOfWork.TournamentRepository.GetApprovalContext(match.TournamentId)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.TournamentNotFound"]);

            bool isManager = await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, user);
            bool isParticipant = BracketService.IsMatchParticipant(match, user.UserId);
            bool isOpen = match.Status != MatchStatus.Completed && match.Status != MatchStatus.NoShow;

            var shown = await this.AppUnitOfWork.MatchResultVerificationRepository.GetShownForMatch(matchId);

            var latestVerified = shown
                .Where(v => v.Status == MatchVerificationStatus.Verified)
                .GroupBy(v => v.UserId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.VerifiedOn).First());

            var players = PlayersOf(match);

            // Device statistics are organizer-only, so they are only computed for an organizer.
            var organizer = isManager
                ? await this.LoadOrganizerDeviceContext(shown, players)
                : OrganizerDeviceContext.Empty;

            var records = new List<MatchVerificationRecordDto>();

            foreach (Guid playerId in players)
            {
                if (latestVerified.TryGetValue(playerId, out var record))
                {
                    records.Add(this.MapRecord(record, user.UserId, isManager, organizer));
                    continue;
                }

                // Not verified (yet). The row is still drawn — "your opponent has not verified" is half
                // of what the panel is for — so it needs a name without a record to take one from.
                var player = await this.AppUnitOfWork.UserRepository.GetById(playerId);
                records.Add(new MatchVerificationRecordDto
                {
                    UserId = playerId,
                    Username = player?.Username ?? string.Empty,
                    AvatarUrl = player?.AvatarUrl,
                    Status = MatchVerificationStatus.Started,
                });
            }

            // Failed attempts are an organizer's business: a player sees their own through the error
            // they were shown at the time.
            if (isManager)
            {
                records.AddRange(shown
                    .Where(v => v.Status == MatchVerificationStatus.Failed)
                    .Select(v => this.MapRecord(v, user.UserId, isManager, organizer)));
            }

            bool callerVerified = latestVerified.ContainsKey(user.UserId);

            return new MatchVerificationPanelDto
            {
                MatchId = matchId,
                Required = settings.RequireResultVerification,
                IsManager = isManager,
                // One verification per player (MaxVerifiedPerMatch): once theirs is in, nothing is left to do.
                CanVerify = settings.RequireResultVerification && isParticipant && isOpen && !callerVerified,
                // Mirrors the gate in BracketService.UpdateMatchResultCore exactly: participants who are
                // not organizers, on a match still open for a result, without a verified record.
                ReportBlocked = settings.RequireResultVerification && isParticipant && !isManager && isOpen && !callerVerified,
                Mine = callerVerified
                    ? this.MapRecord(latestVerified[user.UserId], user.UserId, isManager, organizer)
                    : null,
                Records = records,
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────────

        private async Task<MatchResultVerificationEntity> LoadOwnVerification(Guid verificationId, Guid userId)
        {
            var verification = await this.AppUnitOfWork.MatchResultVerificationRepository.GetForUpdate(verificationId);

            // Someone else's attempt answers exactly like a missing one: its id says nothing to them.
            if (verification == null || verification.UserId != userId)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationNotFound"]);

            return verification;
        }

        /// <summary>The caller's own record, as the step that just ran left it — the flow's answer.</summary>
        private async Task<MatchVerificationRecordDto> BuildOwnRecord(MatchResultVerificationEntity verification)
        {
            // Read separately and handed to the mapper, rather than hung on the entity's navigations: the
            // entity is still tracked by this request, and a detached graph attached to it is a save
            // waiting to go wrong.
            var user = await this.AppUnitOfWork.UserRepository.GetById(verification.UserId);
            var evidence = verification.MatchEvidenceId.HasValue
                ? await this.AppUnitOfWork.MatchEvidenceRepository.GetById(verification.MatchEvidenceId.Value)
                : null;
            var device = verification.UserDeviceId.HasValue
                ? await this.AppUnitOfWork.UserDeviceRepository.GetById(verification.UserDeviceId.Value)
                : null;

            return this.MapRecord(
                verification,
                user,
                evidence,
                device,
                verification.UserId,
                isManager: false,
                OrganizerDeviceContext.Empty);
        }

        /// <summary>
        /// What an organizer's view adds to the records: every account on each phone, their names, and
        /// each account's phone history. Empty for anyone else, which keeps it off a player's screen.
        /// </summary>
        private sealed record OrganizerDeviceContext(
            IReadOnlyDictionary<Guid, List<DeviceAccountSighting>> AccountsOnPhone,
            IReadOnlyDictionary<Guid, UserEntity?> Accounts,
            IReadOnlyDictionary<Guid, VerificationDeviceHistory> DeviceHistory)
        {
            public static readonly OrganizerDeviceContext Empty = new(
                new Dictionary<Guid, List<DeviceAccountSighting>>(),
                new Dictionary<Guid, UserEntity?>(),
                new Dictionary<Guid, VerificationDeviceHistory>());
        }

        private async Task<OrganizerDeviceContext> LoadOrganizerDeviceContext(
            List<MatchResultVerificationEntity> shown,
            List<Guid> players)
        {
            var accountsOnPhone = await this.AppUnitOfWork.UserDeviceRepository.GetAccountsOnSamePhone(
                shown.Where(v => v.UserDeviceId.HasValue).Select(v => v.UserDeviceId!.Value));

            // The names behind "shared phone" — a handful at most: two players, a phone or two each.
            // Shallow: a name and an avatar are all that is read, the role join is not needed.
            var accounts = new Dictionary<Guid, UserEntity?>();
            foreach (Guid accountId in accountsOnPhone.Values.SelectMany(onPhone => onPhone).Select(s => s.UserId).Distinct())
                accounts[accountId] = await this.AppUnitOfWork.UserRepository.ShallowGetById(accountId);

            var history = await this.AppUnitOfWork.UserDeviceRepository.GetDeviceHistoryForUsers(
                players.Concat(shown.Select(v => v.UserId)));

            return new OrganizerDeviceContext(accountsOnPhone, accounts, history);
        }

        private MatchVerificationRecordDto MapRecord(
            MatchResultVerificationEntity v,
            Guid callerId,
            bool isManager,
            OrganizerDeviceContext organizer)
            => this.MapRecord(v, v.User, v.MatchEvidence, v.UserDevice, callerId, isManager, organizer);

        private MatchVerificationRecordDto MapRecord(
            MatchResultVerificationEntity v,
            UserEntity? user,
            MatchEvidenceEntity? evidence,
            UserDeviceEntity? device,
            Guid callerId,
            bool isManager,
            OrganizerDeviceContext organizer)
        {
            bool showDevice = isManager || v.UserId == callerId;
            var history = v.UserDeviceId.HasValue && organizer.DeviceHistory.TryGetValue(v.UserDeviceId.Value, out var found)
                ? found : null;

            var dto = new MatchVerificationRecordDto
            {
                Id = v.Id,
                UserId = v.UserId,
                Username = user?.Username ?? string.Empty,
                AvatarUrl = user?.AvatarUrl,
                Status = v.Status,
                BiometricVerified = v.BiometricVerifiedOn.HasValue,
                StartedOn = v.CreatedOn,
                BiometricVerifiedOn = v.BiometricVerifiedOn,
                VerifiedOn = v.VerifiedOn,
                Evidence = evidence?.Url is { Length: > 0 } url
                    ? new MatchEvidenceItemDto { Url = url, MediaType = evidence.MediaType }
                    : null,
                // The id outlives the clip: retention retires the row, the record keeps pointing at it.
                EvidenceExpired = v.MatchEvidenceId.HasValue && evidence == null,
                EvidenceDurationMs = v.EvidenceDurationMs,
                RecordedOn = v.RecordedOn,
                FailureReason = isManager ? v.FailureReason : null,
            };

            if (showDevice)
            {
                var onPhone = v.UserDeviceId.HasValue && organizer.AccountsOnPhone.TryGetValue(v.UserDeviceId.Value, out var sightings)
                    ? sightings
                    : new List<DeviceAccountSighting>();

                // When this account first appeared on the phone, matched by the same rule as the others.
                DateTime? ownSince = onPhone.FirstOrDefault(s => s.UserId == v.UserId)?.FirstSeenOn;

                var others = onPhone
                    .Where(s => s.UserId != v.UserId)
                    .Select(s =>
                    {
                        var account = organizer.Accounts.GetValueOrDefault(s.UserId);
                        return new MatchVerificationDeviceAccountDto
                        {
                            UserId = s.UserId,
                            Username = account?.Username ?? string.Empty,
                            AvatarUrl = account?.AvatarUrl,
                            FirstSeenOn = s.FirstSeenOn,
                            CameFirst = s.FirstSeenOn is DateTime theirs && ownSince is DateTime ours && theirs < ours,
                        };
                    })
                    .ToList();

                dto.Device = new MatchVerificationDeviceDto
                {
                    DeviceId = v.DeviceId,
                    Platform = v.Platform,
                    DeviceModel = v.DeviceModel,
                    OsVersion = v.OsVersion,
                    AppVersion = v.AppVersion,
                    IsPhysicalDevice = v.IsPhysicalDevice,
                    FirstSeenOn = history?.FirstSeenOn ?? device?.CreatedOn,
                    KeyIssuedOn = v.DeviceKeyIssuedOn,
                    OtherAccountsOnDevice = others.Count,
                    OtherAccounts = others,
                    AccountDeviceCount = history?.AccountDeviceCount ?? 0,
                };
            }

            if (isManager)
                dto.Flags = BuildFlags(v, dto.Device, history?.HasPreviousDevice == true);

            return dto;
        }

        /// <summary>
        /// The organizer's reasons to look twice, all relative to when the attempt started. None of them
        /// is proof of anything — a new phone is usually just a new phone — which is why they are shown
        /// as flags beside the record rather than turned into a verdict.
        /// </summary>
        public static List<string> BuildFlags(MatchResultVerificationEntity v, MatchVerificationDeviceDto? device, bool hasPreviousDevice = false)
        {
            var flags = new List<string>();
            DateTime startedOn = v.CreatedOn ?? v.VerifiedOn ?? DateTime.UtcNow;

            bool recentlyFirstSeen = device?.FirstSeenOn is DateTime firstSeen && startedOn - firstSeen < NewDeviceAge;
            if (recentlyFirstSeen && hasPreviousDevice) flags.Add(FlagNewDevice);

            // The initial key is expected even when this is the account's first phone and no new-device
            // warning is shown. Reinstalling an established phone still correctly exposes a fresh key.
            if (!recentlyFirstSeen && v.DeviceKeyIssuedOn is DateTime keyIssued && startedOn - keyIssued < FreshKeyAge)
                flags.Add(FlagFreshKey);

            if (device != null && device.OtherAccountsOnDevice > 0) flags.Add(FlagSharedDevice);

            if (!v.IsPhysicalDevice) flags.Add(FlagEmulator);

            if (v.RecordedOn is DateTime recorded && (v.VerifiedOn ?? startedOn) - recorded > OldRecordingAge)
                flags.Add(FlagOldRecording);

            return flags;
        }

        /// <summary>
        /// Takes one issuance slot in the window before the key exists. The decision is the value the
        /// atomic increment returns: a read followed by a separate increment let concurrent registrations
        /// all see the same count and all pass. Policy: every registration that gets this far spends its
        /// slot — refused ones and ones that then fail to save included — because the limit is on
        /// attempts to mint keys, and a caller hammering it keeps hitting the cap until the window ends.
        /// Fail-open like the login throttle (AuthThrottleService): it guards against a runaway loop, and
        /// a cache outage must not stop players from verifying.
        /// </summary>
        private async Task<bool> TryReserveKeyIssue(Guid userId)
        {
            try { return await this.cacheService.IncrementAsync(KeyIssueCounterKey(userId), KeyIssueWindow) <= MaxKeysIssuedPerDay; }
            catch { return true; }
        }

        /// <summary>The players of the match — the two people a verification can come from.</summary>
        private static List<Guid> PlayersOf(MatchEntity match)
        {
            var players = new List<Guid?>();

            if (match.TeamMatchId.HasValue)
            {
                players.Add(match.HomeUserId);
                players.Add(match.AwayUserId);
            }
            else
            {
                players.Add(match.HomeParticipant?.UserId);
                players.Add(match.AwayParticipant?.UserId);
            }

            return players.Where(p => p.HasValue).Select(p => p!.Value).Distinct().ToList();
        }

        private void ThrowIfMatchClosed(MatchEntity match)
        {
            if (match.Status == MatchStatus.Completed)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchAlreadyCompleted"]);

            if (match.Status == MatchStatus.NoShow)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchClosedNoShow"]);
        }

        private static string? NormalizePlatform(string? platform)
        {
            string value = (platform ?? string.Empty).Trim().ToLowerInvariant();
            return value is "ios" or "android" ? value : null;
        }

        private static string? Truncate(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string trimmed = value.Trim();
            return trimmed.Length <= max ? trimmed : trimmed[..max];
        }

        /// <summary>
        /// A phone-reported moment, kept only when it is plausible: not in the future (beyond a little
        /// clock drift) and not before smartphones recorded video worth checking. Anything else is
        /// dropped rather than shown to an organizer as if it meant something.
        /// </summary>
        private static DateTime? SanitizePastTimestamp(DateTime? value, DateTime now)
        {
            if (value == null) return null;

            DateTime utc = value.Value.Kind == DateTimeKind.Local
                ? value.Value.ToUniversalTime()
                : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

            if (utc > now.AddMinutes(10) || utc < new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc)) return null;

            return utc;
        }
    }
}
