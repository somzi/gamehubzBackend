using GameHubz.DataModels.Enums;
using GameHubz.Logic.Utility;

namespace GameHubz.Logic.Services
{
    public class TournamentVerificationPhoneService : AppBaseService
    {
        private readonly TournamentAuthorizationService tournamentAuth;
        private readonly INotificationService notificationService;
        private readonly BadgeService badgeService;
        private readonly ICacheService cache;

        public TournamentVerificationPhoneService(IUnitOfWorkFactory factory, IUserContextReader userContextReader,
            ILocalizationService localizationService, TournamentAuthorizationService tournamentAuth,
            INotificationService notificationService, BadgeService badgeService, ICacheService cache)
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.tournamentAuth = tournamentAuth;
            this.notificationService = notificationService;
            this.badgeService = badgeService;
            this.cache = cache;
        }

        public static bool SamePhone(UserDeviceEntity? first, UserDeviceEntity? second) =>
            first != null && second != null &&
            ((first.Id.HasValue && first.Id == second.Id)
                || (!string.IsNullOrWhiteSpace(first.PlatformDeviceIdHash)
                    && first.PlatformDeviceIdHash == second.PlatformDeviceIdHash));

        /// <summary>
        /// Records the phone the player registered for the tournament from, right after the join. No
        /// biometric step: what ties a player to a phone is which phone it is, and Face ID / fingerprint
        /// would only prove that the phone's own owner unlocked it. Biometrics stay on every verification.
        /// The phone's word is enough here because only this account's own phones can be named.
        /// </summary>
        public async Task<TournamentPhoneBindingDto> Bind(Guid tournamentId, RegisterVerificationDeviceRequest request)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetApprovalContext(tournamentId)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.TournamentNotFound"]);
            if (!tournament.RequireResultVerification)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationNotEnabled"]);
            if (!await this.AppUnitOfWork.TournamentPlayerDeviceRepository.IsRegistered(tournamentId, user.UserId))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationPhoneRegistrationRequired"]);

            var device = await this.RegisteringPhone(user.UserId, request);
            return new TournamentPhoneBindingDto(device.DeviceId, await this.BindOrCheck(tournamentId, device));
        }

        /// <summary>
        /// This account's row for the registering phone. A phone that has never verified gets a row with
        /// no key yet (empty <see cref="UserDeviceEntity.KeySecret"/>); its first verification registers
        /// with the same installation id and the key is issued on this row, so the binding already points
        /// at it. An existing row is only read: writing it back could put an old key over a fresh one.
        /// </summary>
        private async Task<UserDeviceEntity> RegisteringPhone(Guid userId, RegisterVerificationDeviceRequest request)
        {
            Guid? installation = request.DeviceId is Guid known && known != Guid.Empty ? known : null;
            if (installation.HasValue && await this.AppUnitOfWork.UserDeviceRepository.GetForUser(userId, installation.Value) is { } existing)
                return existing;

            string platform = MatchVerificationService.NormalizePlatform(request.Platform)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationUnsupportedPlatform"]);

            // A new row is a new phone either way, so it spends from the same daily budget as a key.
            if (!await MatchVerificationService.TryReserveKeyIssue(this.cache, userId))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationTooManyDevices"]);

            DateTime now = DateTime.UtcNow;
            var device = new UserDeviceEntity
            {
                UserId = userId,
                DeviceId = installation ?? Guid.NewGuid(),
                Platform = platform,
                DeviceModel = MatchVerificationService.Truncate(request.DeviceModel, 128),
                DeviceBrand = MatchVerificationService.Truncate(request.DeviceBrand, 64),
                OsVersion = MatchVerificationService.Truncate(request.OsVersion, 64),
                AppVersion = MatchVerificationService.Truncate(request.AppVersion, 32),
                IsPhysicalDevice = request.IsPhysicalDevice,
                PlatformDeviceIdHash = VerificationProof.HashPlatformDeviceId(request.PlatformDeviceId),
                AppInstalledOn = MatchVerificationService.SanitizePastTimestamp(request.AppInstalledOn, now),
                KeySecret = string.Empty,
                KeyIssuedOn = now,
                LastSeenOn = now,
            };
            await this.AppUnitOfWork.UserDeviceRepository.AddEntity(device, this.UserContextReader);
            await this.SaveAsync();
            return device;
        }

        public async Task<bool> IsApprovalPending(Guid tournamentId, Guid userId, Guid? deviceId)
        {
            if (!deviceId.HasValue) return false;
            var device = await this.AppUnitOfWork.UserDeviceRepository.GetForUser(userId, deviceId.Value);
            if (device == null) return false;
            var rows = await this.AppUnitOfWork.TournamentPlayerDeviceRepository.GetForPlayer(tournamentId, userId);
            return !rows.Any(x => x.Status == TournamentPlayerDeviceStatus.Active && SamePhone(x.UserDevice, device))
                && rows.Any(x => x.Status == TournamentPlayerDeviceStatus.Pending && SamePhone(x.UserDevice, device));
        }

        /// <summary>
        /// Whether another phone has taken this one's place for the player in the tournament. Read only —
        /// never binds — for the steps after Start: an attempt begun on a phone the organizer has since
        /// replaced does not go on to finish on it. No active phone at all contradicts nothing.
        /// </summary>
        public async Task<bool> IsReplaced(Guid tournamentId, UserDeviceEntity device)
        {
            var rows = await this.AppUnitOfWork.TournamentPlayerDeviceRepository.GetForPlayer(tournamentId, device.UserId);
            var active = rows.SingleOrDefault(x => x.Status == TournamentPlayerDeviceStatus.Active);
            return active != null && !SamePhone(active.UserDevice, device);
        }

        // Used by enrollment and by Start. Callers must first establish participation and ownership.
        public async Task<TournamentPlayerDeviceStatus> BindOrCheck(Guid tournamentId, UserDeviceEntity device)
        {
            bool notify = false;
            var result = await this.AppUnitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var repository = this.AppUnitOfWork.TournamentPlayerDeviceRepository;
                await repository.LockPlayer(tournamentId, device.UserId);
                var rows = await repository.GetForPlayer(tournamentId, device.UserId);
                var active = rows.SingleOrDefault(x => x.Status == TournamentPlayerDeviceStatus.Active);
                if (active != null && SamePhone(active.UserDevice, device)) return TournamentPlayerDeviceStatus.Active;

                var previous = await this.AppUnitOfWork.UserDeviceRepository.GetVerifiedForUser(device.UserId);
                bool rejected = rows.Any(x => x.Status == TournamentPlayerDeviceStatus.Rejected && SamePhone(x.UserDevice, device));
                bool needsApproval = active != null || rejected
                    || (previous.Count > 0 && !previous.Any(x => SamePhone(x, device)));
                var pending = rows.SingleOrDefault(x => x.Status == TournamentPlayerDeviceStatus.Pending);
                if (needsApproval && pending?.UserDeviceId == device.Id) return TournamentPlayerDeviceStatus.Pending;

                var binding = needsApproval ? pending : null;
                bool isNew = binding == null;
                binding ??= new TournamentPlayerDeviceEntity { TournamentId = tournamentId, UserId = device.UserId };
                binding.UserDeviceId = device.Id!.Value;
                binding.UserDevice = device;
                binding.Status = needsApproval ? TournamentPlayerDeviceStatus.Pending : TournamentPlayerDeviceStatus.Active;
                binding.RequestedOn = DateTime.UtcNow;
                binding.DecidedOn = needsApproval ? null : binding.RequestedOn;
                binding.DecidedByUserId = null;
                if (isNew) await repository.AddEntity(binding, this.UserContextReader);
                else await repository.UpdateEntity(binding, this.UserContextReader);
                await this.SaveAsync();
                notify = needsApproval;
                return binding.Status;
            });

            if (notify) await this.NotifyRequest(tournamentId, device.UserId);
            return result;
        }

        public async Task<List<TournamentPhoneRequestDto>> GetPending(Guid tournamentId)
        {
            await this.RequireManager(tournamentId);
            var pending = await this.AppUnitOfWork.TournamentPlayerDeviceRepository.GetPending(tournamentId);
            var accounts = await this.AppUnitOfWork.UserDeviceRepository.GetAccountsOnSamePhone(pending.Select(x => x.UserDeviceId));
            var identities = new Dictionary<Guid, UserEntity?>();
            foreach (Guid userId in accounts.Values.SelectMany(x => x).Select(x => x.UserId).Distinct())
                identities[userId] = await this.AppUnitOfWork.UserRepository.GetById(userId);

            var result = new List<TournamentPhoneRequestDto>();
            foreach (var request in pending)
            {
                var rows = await this.AppUnitOfWork.TournamentPlayerDeviceRepository.GetForPlayer(tournamentId, request.UserId);
                var active = rows.SingleOrDefault(x => x.Status == TournamentPlayerDeviceStatus.Active);
                var sightings = accounts.GetValueOrDefault(request.UserDeviceId) ?? new();
                var ownSince = sightings.FirstOrDefault(x => x.UserId == request.UserId)?.FirstSeenOn;
                result.Add(new TournamentPhoneRequestDto
                {
                    Id = request.Id!.Value,
                    UserId = request.UserId,
                    Username = request.User?.Username ?? string.Empty,
                    AvatarUrl = request.User?.AvatarUrl,
                    ActivePhone = active?.UserDevice == null ? null : Phone(active.UserDevice),
                    RequestedPhone = Phone(request.UserDevice!),
                    RequestedOn = request.RequestedOn,
                    OtherAccounts = sightings.Where(x => x.UserId != request.UserId).Select(x => new MatchVerificationDeviceAccountDto
                    {
                        UserId = x.UserId,
                        Username = identities.GetValueOrDefault(x.UserId)?.Username ?? string.Empty,
                        AvatarUrl = identities.GetValueOrDefault(x.UserId)?.AvatarUrl,
                        FirstSeenOn = x.FirstSeenOn,
                        CameFirst = ownSince.HasValue && x.FirstSeenOn < ownSince.Value,
                    }).ToList(),
                });
            }
            return result;
        }

        public async Task Decide(Guid tournamentId, Guid requestId, TournamentPhoneDecisionRequest decision, bool approve)
        {
            var managerId = await this.RequireManager(tournamentId);
            var pending = (await this.AppUnitOfWork.TournamentPlayerDeviceRepository.GetPending(tournamentId))
                .SingleOrDefault(x => x.Id == requestId)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationPhoneRequestChanged"]);
            await this.AppUnitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var repository = this.AppUnitOfWork.TournamentPlayerDeviceRepository;
                await repository.LockPlayer(tournamentId, pending.UserId);
                var rows = await repository.GetForPlayer(tournamentId, pending.UserId);
                var current = rows.SingleOrDefault(x => x.Id == requestId && x.Status == TournamentPlayerDeviceStatus.Pending);
                if (current == null || current.UserDeviceId != decision.UserDeviceId)
                    throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationPhoneRequestChanged"]);

                DateTime now = DateTime.UtcNow;
                if (approve)
                {
                    var active = rows.SingleOrDefault(x => x.Status == TournamentPlayerDeviceStatus.Active);
                    if (active != null)
                    {
                        active.Status = TournamentPlayerDeviceStatus.Replaced;
                        await repository.UpdateEntity(active, this.UserContextReader);
                        // Release the partial unique index slot before activating the replacement.
                        await this.SaveAsync();
                    }
                }
                current.Status = approve ? TournamentPlayerDeviceStatus.Active : TournamentPlayerDeviceStatus.Rejected;
                current.DecidedOn = now;
                current.DecidedByUserId = managerId;
                await repository.UpdateEntity(current, this.UserContextReader);
                await this.SaveAsync();
                return true;
            });

            await this.badgeService.PushToTournamentManagersAsync(tournamentId);
            var player = await this.AppUnitOfWork.UserRepository.GetById(pending.UserId);
            if (player != null)
                FireAndForgetPush(new() { PushRecipient.ForUser(pending.UserId, player.PushToken, player.Language) },
                    PushText.FromKey(approve ? "Push.VerificationPhoneApproved.Title" : "Push.VerificationPhoneRejected.Title"),
                    PushText.FromKey(approve ? "Push.VerificationPhoneApproved.Body" : "Push.VerificationPhoneRejected.Body"),
                    new { tournamentId = tournamentId.ToString(), type = approve ? "verificationPhoneApproved" : "verificationPhoneRejected" });
        }

        private async Task<Guid> RequireManager(Guid tournamentId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId, user))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.VerificationPhoneManagerRequired"]);
            return user.UserId;
        }

        private static TournamentPhoneDto Phone(UserDeviceEntity device) => new()
            { UserDeviceId = device.Id!.Value, Platform = device.Platform, DeviceModel = device.DeviceModel };

        private async Task NotifyRequest(Guid tournamentId, Guid userId)
        {
            await this.badgeService.PushToTournamentManagersAsync(tournamentId);
            var recipients = await CollectHubAdminPushTokensAsync(tournamentId, userId);
            if (recipients.Count == 0) return;
            var player = await this.AppUnitOfWork.UserRepository.GetById(userId);
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetById(tournamentId);
            FireAndForgetPush(recipients,
                tournament?.Name is { Length: > 0 } name ? PushText.FromLiteral(name) : PushText.FromKey("Push.VerificationPhoneRequested.Title"),
                PushText.FromKey("Push.VerificationPhoneRequested.Body", player?.Username ?? string.Empty),
                new { tournamentId = tournamentId.ToString(), type = "verificationPhoneRequested" });
        }

        private async Task<List<PushRecipient>> CollectHubAdminPushTokensAsync(Guid tournamentId, Guid excludeUserId)
        {
            var ownership = await this.AppUnitOfWork.TournamentRepository.GetHubOwnership(tournamentId);
            if (ownership == null) return new();
            var members = await this.AppUnitOfWork.UserHubRepository.GetUsersByHub(ownership.HubId);
            var recipients = members.Where(x => x.UserId != excludeUserId && (x.HubRole == HubRole.HubOwner || x.HubRole == HubRole.HubAdmin))
                .Select(x => PushRecipient.ForUser(x.UserId, x.PushToken, x.Language)).ToList();
            if (ownership.OwnerUserId != excludeUserId && !members.Any(x => x.UserId == ownership.OwnerUserId))
            {
                var owner = await this.AppUnitOfWork.UserRepository.GetById(ownership.OwnerUserId);
                if (owner != null) recipients.Add(PushRecipient.ForUser(ownership.OwnerUserId, owner.PushToken, owner.Language));
            }
            return recipients;
        }

        private void FireAndForgetPush(List<PushRecipient> recipients, PushText title, PushText body, object data)
        {
            if (recipients.Count == 0) return;
            _ = Task.Run(async () =>
            {
                try { await this.notificationService.SendLocalizedToManyAsync(recipients, title, body, data); }
                catch { /* The persisted request remains in the organizer inbox. */ }
            });
        }
    }
}
