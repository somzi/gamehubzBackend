using GameHubz.DataModels.Consts;
using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Tournament invites: a manager asks a player into a solo tournament, and the player becomes a
    /// participant only by accepting. Works in every registration mode — in an invite-only
    /// tournament it is the only way in. Swap invites (another participant's spot) are sent by
    /// <see cref="TournamentParticipantService.SendSwapInvite"/> and answered here like any other.
    ///
    /// Rules: one open invite per player per tournament; a player who declined is never asked into the
    /// same tournament again; a hub ban always keeps a player out; a player outside the tournament's
    /// country / region / exclusive scope only through an invite sent knowingly as a guest. An invite
    /// reserves no place — accepting checks the room under the tournament's entry lock.
    /// </summary>
    public class TournamentInviteService : AppBaseService
    {
        private const int CandidatePageSize = 20;

        // A technical ceiling on one request, not a limit on how many players can be invited: the
        // organizer can send as many batches as they like.
        private const int MaxInvitesPerRequest = 100;

        private readonly TournamentAuthorizationService tournamentAuth;
        private readonly TournamentRegistrationService registrationService;
        private readonly TournamentParticipantService participantService;
        private readonly UserHubRequestService userHubRequestService;
        private readonly INotificationService notificationService;

        public TournamentInviteService(
            IUnitOfWorkFactory factory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            TournamentAuthorizationService tournamentAuth,
            TournamentRegistrationService registrationService,
            TournamentParticipantService participantService,
            UserHubRequestService userHubRequestService,
            INotificationService notificationService)
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.tournamentAuth = tournamentAuth;
            this.registrationService = registrationService;
            this.participantService = participantService;
            this.userHubRequestService = userHubRequestService;
            this.notificationService = notificationService;
        }

        // ── Organizer ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Players the organizer can pick: hub members until something is typed, then everyone whose
        /// username matches. Entrants, banned players and the caller are already left out; each row
        /// says whether the player is inside the tournament's scope (or can only come as a guest) and
        /// whether they hold an invite or declined one.
        /// </summary>
        public async Task<List<TournamentInviteCandidateDto>> GetCandidates(Guid tournamentId, string? search, bool onlyEligible, int pageNumber)
        {
            await this.EnsureCanManage(tournamentId);

            var tournament = await this.AppUnitOfWork.TournamentRepository.ShallowGetById(tournamentId);
            if (tournament?.HubId == null || tournament.IsTeamTournament)
            {
                return new List<TournamentInviteCandidateDto>();
            }

            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            Guid hubId = tournament.HubId.Value;

            var users = await this.AppUnitOfWork.TournamentInviteRepository.GetCandidatesPaged(
                tournamentId,
                hubId,
                caller.UserId,
                search,
                onlyEligible,
                tournament.Countries,
                tournament.Region,
                tournament.IsExclusive,
                Math.Max(0, pageNumber),
                CandidatePageSize);

            var userIds = users.Select(u => u.Id!.Value).ToList();
            var states = await this.AppUnitOfWork.TournamentInviteRepository.GetInviteStates(tournamentId, userIds);
            var exclusiveIds = tournament.IsExclusive
                ? await this.AppUnitOfWork.TournamentInviteRepository.GetUserIdsWithExclusiveAccess(hubId, userIds)
                : new HashSet<Guid>();

            return users.Select(u => new TournamentInviteCandidateDto
            {
                UserId = u.Id!.Value,
                Username = u.Username,
                Nickname = u.Nickname,
                AvatarUrl = u.AvatarUrl,
                Country = u.Country,
                MeetsRequirements = MeetsRequirements(tournament, u, exclusiveIds),
                InviteState = states.TryGetValue(u.Id!.Value, out var state)
                    ? (state == TournamentInviteStatus.Pending ? "pending" : "declined")
                    : "none",
            }).ToList();
        }

        /// <summary>
        /// Sends one invite per player. Each player gets an outcome rather than the whole batch failing
        /// on the first one that can't be invited; only "sent" rows created an invite and a notification.
        /// </summary>
        public async Task<TournamentInviteSendResultDto> SendInvites(Guid tournamentId, SendTournamentInvitesRequest request)
        {
            await this.EnsureCanManage(tournamentId);

            var targets = (request?.Invites ?? new List<TournamentInviteTarget>())
                .Where(t => t.UserId != Guid.Empty)
                .GroupBy(t => t.UserId)
                .Select(g => g.First())
                .ToList();

            var result = new TournamentInviteSendResultDto();
            if (targets.Count == 0)
            {
                return result;
            }

            if (targets.Count > MaxInvitesPerRequest)
            {
                throw new BusinessRuleException(string.Format(this.LocalizationService["BusinessRule.TooManyInvitesAtOnce"], MaxInvitesPerRequest));
            }

            var tournament = await this.AppUnitOfWork.TournamentRepository.ShallowGetById(tournamentId)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.InviteTournamentGone"]);

            if (tournament.IsTeamTournament)
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.InvitesSoloOnly"]);
            }

            // Open, or scheduled to open. A Draft without an opening time is a tournament the organizer is
            // still writing and nobody else can see yet, so there is nothing to invite anyone into.
            if (!TournamentInviteRules.IsAnswerable(TournamentInviteKind.Entry, tournament.Status)
                || (tournament.Status == TournamentStatus.Draft && tournament.RegistrationOpensAt == null))
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.InvitesRegistrationClosed"]);
            }

            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var userIds = targets.Select(t => t.UserId).ToList();

            var states = await this.AppUnitOfWork.TournamentInviteRepository.GetInviteStates(tournamentId, userIds);
            var entrants = (await this.AppUnitOfWork.TournamentParticipantRepository.GetAllUserIdsByTournamentId(tournamentId)).ToHashSet();
            var banned = tournament.HubId.HasValue
                ? (await this.AppUnitOfWork.UserHubBanRepository.GetBannedUserIds(tournament.HubId.Value, userIds)).ToHashSet()
                : new HashSet<Guid>();
            var exclusiveIds = tournament.IsExclusive && tournament.HubId.HasValue
                ? await this.AppUnitOfWork.TournamentInviteRepository.GetUserIdsWithExclusiveAccess(tournament.HubId.Value, userIds)
                : new HashSet<Guid>();

            var sent = new List<(UserEntity User, Guid InviteId)>();

            foreach (var target in targets)
            {
                var user = await this.AppUnitOfWork.UserRepository.ShallowGetById(target.UserId);
                var item = new TournamentInviteSendItemDto { UserId = target.UserId, Username = user?.Username ?? string.Empty };
                result.Items.Add(item);

                if (user == null || !user.IsActive)
                {
                    item.Outcome = "notFound";
                    continue;
                }

                if (entrants.Contains(target.UserId))
                {
                    item.Outcome = "alreadyIn";
                    continue;
                }

                if (states.TryGetValue(target.UserId, out var state))
                {
                    item.Outcome = state == TournamentInviteStatus.Pending ? "alreadyInvited" : "declinedBefore";
                    continue;
                }

                if (banned.Contains(target.UserId))
                {
                    item.Outcome = "banned";
                    continue;
                }

                bool meets = MeetsRequirements(tournament, user, exclusiveIds);
                if (!meets && !target.AsGuest)
                {
                    item.Outcome = "needsGuest";
                    continue;
                }

                var invite = new TournamentInviteEntity
                {
                    TournamentId = tournamentId,
                    UserId = target.UserId,
                    InvitedByUserId = caller.UserId,
                    Kind = TournamentInviteKind.Entry,
                    Status = TournamentInviteStatus.Pending,
                    // Only a player who really is outside the scope is a guest; "as guest" on someone
                    // inside it changes nothing.
                    IsGuest = !meets,
                };

                await this.AppUnitOfWork.TournamentInviteRepository.AddEntity(invite, this.UserContextReader);

                try
                {
                    // One row per save: a unique clash on one player (a second manager inviting them a
                    // moment earlier) must not take the rest of the batch down with it.
                    await this.SaveAsync();
                }
                catch (Exception ex) when (IsUniqueViolation(ex))
                {
                    await this.AppUnitOfWork.TournamentInviteRepository.DetachEntity(invite);
                    item.Outcome = "alreadyInvited";
                    continue;
                }

                item.Outcome = "sent";
                sent.Add((user, invite.Id!.Value));
            }

            if (sent.Count > 0)
            {
                string from = await this.ResolveOrganizerName(tournament, caller.UserId);
                var pushes = sent
                    .Select(s => new LocalizedPush(
                        new[] { PushRecipient.ForUser(s.User.Id!.Value, s.User.PushToken, s.User.Language) },
                        PushText.FromLiteral(tournament.Name),
                        PushText.FromKey("Push.TournamentInvite.Body", from),
                        new { tournamentId = tournamentId.ToString(), inviteId = s.InviteId.ToString(), type = "tournamentInvite" }))
                    .ToList();

                // Fire-and-forget like every push: the invites are committed, a failed send must not
                // turn the answer into an error. The inbox rows are written by the send itself.
                _ = Task.Run(async () =>
                {
                    try { await this.notificationService.SendLocalizedBatchAsync(pushes); }
                    catch { /* fire-and-forget */ }
                });
            }

            return result;
        }

        /// <summary>Every invite of the tournament, newest first, with the status as it stands now.</summary>
        public async Task<List<TournamentInviteDto>> GetTournamentInvites(Guid tournamentId)
        {
            await this.EnsureCanManage(tournamentId);

            var tournament = await this.AppUnitOfWork.TournamentRepository.ShallowGetById(tournamentId);
            var invites = await this.AppUnitOfWork.TournamentInviteRepository.GetForTournament(tournamentId);

            foreach (var invite in invites)
            {
                invite.Status = TournamentInviteRules.EffectiveStatus(invite.Status, invite.Kind, tournament?.Status);
            }

            return invites;
        }

        /// <summary>Withdraws an open invite. Silent — the player is not notified.</summary>
        public async Task CancelInvite(Guid inviteId)
        {
            var invite = await this.AppUnitOfWork.TournamentInviteRepository.ShallowGetById(inviteId)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.InviteNotFound"]);

            await this.EnsureCanManage(invite.TournamentId);

            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            if (!await this.AppUnitOfWork.TournamentInviteRepository.TryTransition(
                inviteId, TournamentInviteStatus.Pending, TournamentInviteStatus.Cancelled, DateTime.UtcNow, caller.UserId))
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.InviteNoLongerOpen"]);
            }
        }

        // ── Invited player ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The caller's latest invite to this tournament, with what the banner needs to say about it,
        /// or null when they were never invited.
        /// </summary>
        public async Task<MyTournamentInviteDto?> GetMyInvite(Guid tournamentId)
        {
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var invite = await this.AppUnitOfWork.TournamentInviteRepository.GetLatest(tournamentId, caller.UserId);
            if (invite == null)
            {
                return null;
            }

            var tournament = await this.AppUnitOfWork.TournamentRepository.ShallowGetById(tournamentId);
            var status = TournamentInviteRules.EffectiveStatus(invite.Status, invite.Kind, tournament?.Status);

            var dto = new MyTournamentInviteDto
            {
                Id = invite.Id!.Value,
                Kind = invite.Kind,
                Status = status,
                IsGuest = invite.IsGuest,
            };

            if (invite.InvitedByUserId.HasValue)
            {
                dto.InvitedByUsername = (await this.AppUnitOfWork.UserRepository.ShallowGetById(invite.InvitedByUserId.Value))?.Username;
            }

            if (status != TournamentInviteStatus.Pending || tournament == null)
            {
                return dto;
            }

            if (invite.Kind == TournamentInviteKind.Swap && invite.SwapOutgoingUserId.HasValue)
            {
                dto.SwapOutgoingUsername = (await this.AppUnitOfWork.UserRepository.ShallowGetById(invite.SwapOutgoingUserId.Value))?.Username;
                dto.SwapPlayedMatches = await this.participantService.CountPlayedMatchesOfSpot(tournamentId, invite.SwapOutgoingUserId.Value);
            }
            else
            {
                dto.RegistrationNotOpenYet = tournament.Status == TournamentStatus.Draft;
                dto.IsTournamentFull = tournament.MaxPlayers.HasValue
                    && await this.AppUnitOfWork.TournamentParticipantRepository.CountByTournament(tournamentId) >= tournament.MaxPlayers.Value;
            }

            return dto;
        }

        /// <summary>
        /// Accepts the caller's invite. An entry invite makes them a participant — every rule is
        /// checked again now, and the last place is taken under the tournament's entry lock, so two
        /// players accepting at once can't overfill it. A swap invite hands them the spot.
        /// </summary>
        public async Task AcceptInvite(Guid inviteId)
        {
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var invite = await this.GetOwnOpenInvite(inviteId, caller.UserId);

            var tournament = await this.AppUnitOfWork.TournamentRepository.ShallowGetById(invite.TournamentId);
            DateTime now = DateTime.UtcNow;

            if (!TournamentInviteRules.IsAnswerable(invite.Kind, tournament?.Status))
            {
                await this.AppUnitOfWork.TournamentInviteRepository.TryTransition(
                    inviteId, TournamentInviteStatus.Pending, TournamentInviteStatus.Expired, now, caller.UserId);
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.InviteExpired"]);
            }

            if (invite.Kind == TournamentInviteKind.Swap)
            {
                await this.participantService.AcceptSwapInvite(invite, caller.UserId);
                await this.NotifyInviterAsync(invite, tournament!, caller.UserId, accepted: true);
                return;
            }

            await this.AcceptEntryInvite(invite, tournament!, caller.UserId, now);
            await this.NotifyInviterAsync(invite, tournament!, caller.UserId, accepted: true);
        }

        /// <summary>Declines the caller's invite. Final: the same tournament can't invite them again.</summary>
        public async Task DeclineInvite(Guid inviteId)
        {
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var invite = await this.GetOwnOpenInvite(inviteId, caller.UserId);

            if (!await this.AppUnitOfWork.TournamentInviteRepository.TryTransition(
                inviteId, TournamentInviteStatus.Pending, TournamentInviteStatus.Declined, DateTime.UtcNow, caller.UserId))
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.InviteNoLongerOpen"]);
            }

            var tournament = await this.AppUnitOfWork.TournamentRepository.ShallowGetById(invite.TournamentId);
            if (tournament != null)
            {
                await this.NotifyInviterAsync(invite, tournament, caller.UserId, accepted: false);
            }
        }

        private async Task AcceptEntryInvite(TournamentInviteEntity invite, TournamentEntity tournament, Guid userId, DateTime now)
        {
            Guid tournamentId = tournament.Id!.Value;

            if (tournament.Status == TournamentStatus.Draft)
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.RegistrationNotOpenedYet"]);
            }

            // A ban can be lifted, so the invite stays open — it just can't be used while it stands.
            await HubBans.EnsureCallerNotBannedAsync(this.AppUnitOfWork, this.LocalizationService, tournament.HubId, userId);

            // The same scope a sign-up has to pass — unless the organizer invited this player as a guest.
            if (!invite.IsGuest)
            {
                var user = await this.AppUnitOfWork.UserRepository.ShallowGetByIdOrThrowIfNull(userId);
                if (!IsWithinScope(tournament, user))
                {
                    throw new BusinessRuleException(this.LocalizationService["BusinessRule.TournamentRegionRestricted"]);
                }

                if (tournament.IsExclusive && tournament.HubId.HasValue)
                {
                    var role = await this.AppUnitOfWork.UserHubRepository.GetRole(userId, tournament.HubId.Value);
                    if (role != HubRole.HubOwner && role != HubRole.HubAdmin && role != HubRole.HubExclusive)
                    {
                        throw new BusinessRuleException(this.LocalizationService["BusinessRule.TournamentExclusiveOnly"]);
                    }
                }
            }

            bool closedMeanwhile = false;
            bool admitted;
            try
            {
                admitted = await this.AdmitUnderEntryLock(invite, tournament, userId, now, () => closedMeanwhile = true);
            }
            catch (BusinessRuleException) when (closedMeanwhile)
            {
                // Registration closed (or the bracket was drawn) while this request waited: the invite
                // is over, and is written down as such.
                await this.AppUnitOfWork.TournamentInviteRepository.TryTransition(
                    invite.Id!.Value, TournamentInviteStatus.Pending, TournamentInviteStatus.Expired, now, userId);
                throw;
            }

            if (!admitted)
            {
                return;
            }

            await this.registrationService.InvalidateAfterAdmission(tournamentId, userId);

            // Entering follows the hub, as a sign-up does (first contact only, never fails the entry).
            await this.userHubRequestService.FollowHubOfTournament(tournamentId);
        }

        /// <summary>
        /// The admission itself, under the tournament's entry lock. The status is read again here:
        /// closing registration and the bracket claim take the same lock, so what is read can't change
        /// before this insert commits — a player can never land in a tournament whose draw left them out.
        /// </summary>
        private Task<bool> AdmitUnderEntryLock(TournamentInviteEntity invite, TournamentEntity tournament, Guid userId, DateTime now, Action onClosed)
        {
            Guid tournamentId = tournament.Id!.Value;

            return this.AppUnitOfWork.ExecuteInTransactionAsync(async () =>
            {
                await this.AppUnitOfWork.TournamentRepository.LockEntry(tournamentId);

                var current = await this.AppUnitOfWork.TournamentRepository.ShallowGetById(tournamentId);
                if (current == null || current.Status != TournamentStatus.RegistrationOpen)
                {
                    onClosed();
                    throw new BusinessRuleException(this.LocalizationService["BusinessRule.InviteExpired"]);
                }

                if (await this.AppUnitOfWork.TournamentParticipantRepository.ExistsForUser(tournamentId, userId))
                {
                    // Already in (an approved sign-up got there first): the invite is answered all the same.
                    await this.AppUnitOfWork.TournamentInviteRepository.TryTransition(
                        invite.Id!.Value, TournamentInviteStatus.Pending, TournamentInviteStatus.Accepted, now, userId);
                    return false;
                }

                // Full right now: the invite stays open and works again if a place frees up.
                await this.registrationService.EnsureRoomFor(tournamentId, current.MaxPlayers, 1, "BusinessRule.TournamentFullForInvite");

                if (!await this.AppUnitOfWork.TournamentInviteRepository.TryTransition(
                    invite.Id!.Value, TournamentInviteStatus.Pending, TournamentInviteStatus.Accepted, now, userId))
                {
                    // Cancelled by the organizer (or answered on another device) a moment ago.
                    throw new BusinessRuleException(this.LocalizationService["BusinessRule.InviteNoLongerOpen"]);
                }

                await this.registrationService.AdmitInvitedPlayer(tournamentId, userId);
                return true;
            });
        }

        private async Task<TournamentInviteEntity> GetOwnOpenInvite(Guid inviteId, Guid userId)
        {
            var invite = await this.AppUnitOfWork.TournamentInviteRepository.ShallowGetById(inviteId);

            // Someone else's invite reads as missing: whether it exists is none of the caller's business.
            if (invite == null || invite.UserId != userId)
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.InviteNotFound"]);
            }

            if (invite.Status != TournamentInviteStatus.Pending)
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.InviteNoLongerOpen"]);
            }

            return invite;
        }

        // The organizer hears how it went, in the inbox like any other notification. Best-effort.
        private async Task NotifyInviterAsync(TournamentInviteEntity invite, TournamentEntity tournament, Guid playerUserId, bool accepted)
        {
            try
            {
                if (!invite.InvitedByUserId.HasValue) return;

                var inviter = await this.AppUnitOfWork.UserRepository.ShallowGetById(invite.InvitedByUserId.Value);
                var player = await this.AppUnitOfWork.UserRepository.ShallowGetById(playerUserId);
                if (inviter?.Id == null) return;

                var recipient = PushRecipient.ForUser(inviter.Id.Value, inviter.PushToken, inviter.Language);
                var body = PushText.FromKey(
                    accepted ? "Push.TournamentInvite.Accepted" : "Push.TournamentInvite.Declined",
                    player?.Username ?? string.Empty);
                var data = new
                {
                    tournamentId = tournament.Id!.Value.ToString(),
                    type = accepted ? "tournamentInviteAccepted" : "tournamentInviteDeclined",
                };

                _ = Task.Run(async () =>
                {
                    try { await this.notificationService.SendLocalizedToOneAsync(recipient, PushText.FromLiteral(tournament.Name), body, data); }
                    catch { /* fire-and-forget */ }
                });
            }
            catch
            {
                // A notification never undoes an answer that is already committed.
            }
        }

        // Who the invite comes from, as the player sees it: the hub (the organizer's public face), or
        // the manager's own name for a tournament without one.
        private async Task<string> ResolveOrganizerName(TournamentEntity tournament, Guid callerUserId)
        {
            if (tournament.HubId.HasValue)
            {
                var hub = await this.AppUnitOfWork.HubRepository.ShallowGetById(tournament.HubId.Value);
                if (!string.IsNullOrWhiteSpace(hub?.Name)) return hub!.Name;
            }

            var caller = await this.AppUnitOfWork.UserRepository.ShallowGetById(callerUserId);
            return caller?.Username ?? string.Empty;
        }

        private static bool MeetsRequirements(TournamentEntity tournament, UserEntity user, HashSet<Guid> exclusiveIds)
            => IsWithinScope(tournament, user)
                && (!tournament.IsExclusive || exclusiveIds.Contains(user.Id!.Value));

        /// <summary>
        /// Mirrors TournamentRegistrationService.IsEligibleToJoin — kept as a copy like the feed and the
        /// swap keep theirs: a country-scoped tournament takes only its countries, everything else is
        /// region-scoped, and a GLOBAL tournament takes everyone.
        /// </summary>
        private static bool IsWithinScope(TournamentEntity tournament, UserEntity user)
        {
            if (tournament.Countries != null && tournament.Countries.Count > 0)
            {
                return !string.IsNullOrEmpty(user.Country) && tournament.Countries.Contains(user.Country!);
            }

            return tournament.Region == RegionType.GLOBAL || tournament.Region == user.Region;
        }

        // 400, not 401: the mobile 401 interceptor treats any 401 as an expired session.
        private async Task EnsureCanManage(Guid tournamentId)
        {
            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId))
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyStaffManageTournament"]);
            }
        }

        /// <summary>A Postgres unique violation (SqlState 23505) anywhere in the chain, without a compile-time Npgsql dependency.</summary>
        internal static bool IsUniqueViolation(Exception ex)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                if (e.GetType().Name == "PostgresException"
                    && e.GetType().GetProperty("SqlState")?.GetValue(e) as string == "23505")
                {
                    return true;
                }
            }

            return false;
        }
    }
}
