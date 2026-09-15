using FluentValidation;
using GameHubz.Common.Consts;
using GameHubz.DataModels.Catalog;
using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class TournamentService : AppBaseServiceGeneric<TournamentEntity, TournamentDto, TournamentPost, TournamentEdit>
    {
        private readonly HubActivityService hubActivityService;
        private readonly ICacheService cacheService;
        private readonly TournamentNotifier tournamentNotifier;
        private readonly TournamentAuthorizationService tournamentAuth;
        private readonly UserHubService userHubService;
        private readonly EvidenceRetentionService evidenceRetention;

        public TournamentService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<TournamentEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader,
            HubActivityService hubActivityService,
            ICacheService cacheService,
            TournamentNotifier tournamentNotifier,
            TournamentAuthorizationService tournamentAuth,
            UserHubService userHubService,
            EvidenceRetentionService evidenceRetention) : base(
                factory.CreateAppUnitOfWork(),
                userContextReader,
                localizationService,
                searchService,
                validator,
                mapper,
                serviceFunctions)
        {
            this.hubActivityService = hubActivityService;
            this.cacheService = cacheService;
            this.tournamentNotifier = tournamentNotifier;
            this.tournamentAuth = tournamentAuth;
            this.userHubService = userHubService;
            this.evidenceRetention = evidenceRetention;
        }

        public async Task<TournamentPagedResponse> GetTournamentsPagedForHub(Guid hubId, TournamentRequest request)
        {
            // Unfinished drafts (Draft with no opening time) are the organiser's workbench and are
            // listed only for people who can manage the hub. A Draft that IS scheduled stays public —
            // it has been announced and everyone should see when it opens. The flag is part of the
            // cache key, otherwise a manager's page would be served to members and leak the drafts.
            bool canManageHub = await CanManageHubAsync(hubId);

            string statusKey = request.Status.ToString();
            string cacheKey = $"tournaments:hub:{hubId}:status:{statusKey}:mgr:{canManageHub}:p:{request.Page}:s:{request.PageSize}";

            var cachedResponse = await cacheService.GetAsync<TournamentPagedResponse>(cacheKey);
            if (cachedResponse != null)
            {
                return cachedResponse;
            }

            var tournaments = await this.AppUnitOfWork.TournamentRepository.GetByHubPaged(hubId, request.Status, request.Page, request.PageSize, canManageHub);
            var tournamentsCount = await this.AppUnitOfWork.TournamentRepository.GetByHubCount(hubId, request.Status, canManageHub);

            var response = new TournamentPagedResponse
            {
                Count = tournamentsCount,
                Tournaments = tournaments
            };

            await cacheService.SetAsync(cacheKey, response, TimeSpan.FromSeconds(30));

            return response;
        }

        /// <param name="includeScheduled">
        /// v2 callers only. Surfaces tournaments waiting for their scheduled opening in the
        /// "available to join" tab, where the client shows the opening time instead of a Join
        /// button. v1 keeps them out — an older client would offer a Join the server rejects.
        /// </param>
        public async Task<TournamentPagedResponse> GetTournamentPagedForUser(Guid userId, UserTournamentRequest request, bool includeScheduled = false)
        {
            string statusKey = request.Status.ToString();

            // v1 and v2 return different rows for the same user and tab, so they must not share
            // a cache entry.
            string cacheKey = $"user_feed:{userId}:st:{statusKey}:sch:{includeScheduled}:p:{request.Page}:s:{request.PageSize}";

            var cachedResponse = await cacheService.GetAsync<TournamentPagedResponse>(cacheKey);
            if (cachedResponse != null)
            {
                return cachedResponse;
            }

            List<Guid> hubIds = await this.AppUnitOfWork.HubRepository.GetHubIdsByUserId(userId);

            // Subset of hubIds where the user is Exclusive-or-higher — gates exclusive tournaments.
            List<Guid> exclusiveHubIds = await this.AppUnitOfWork.UserHubRepository.GetHubIdsWithExclusiveAccess(userId);

            // Read region + country from the DB (not the token): selecting a country changes the
            // user's region, and the JWT's region claim can be stale until the next token refresh.
            var userEntity = await this.AppUnitOfWork.UserRepository.ShallowGetByIdOrThrowIfNull(userId);
            var userRegion = userEntity.Region;
            var userCountry = userEntity.Country;

            List<TournamentOverview> tournaments = await this.AppUnitOfWork.TournamentRepository.GetByHubsPaged(userId, hubIds, exclusiveHubIds, request.Status, userRegion, userCountry, request.Page, request.PageSize, includeScheduled);

            var tournamentsCount = await this.AppUnitOfWork.TournamentRepository.GetCountByHubs(userId, hubIds, exclusiveHubIds, userRegion, userCountry, request.Status, includeScheduled);

            var response = new TournamentPagedResponse
            {
                Count = tournamentsCount,
                Tournaments = tournaments
            };

            await cacheService.SetAsync(cacheKey, response, TimeSpan.FromSeconds(30));

            return response;
        }

        public async Task<TournamentDto> GetDetailsById(Guid id)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(id);

            return this.Mapper.Map<TournamentDto>(tournament);
        }

        public async Task CloseRegistration(Guid id)
        {
            // F38: closing registration is a lifecycle transition reserved for tournament managers
            // (compare OpenRegistration/CancelTournament which gate through GetHubOwnedTournamentOrThrow).
            if (!await this.tournamentAuth.CanManageTournamentAsync(id))
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyStaffManageTournament"]);
            }

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithPendingRegistration(id);

            if (tournament.TournamentParticipants != null && tournament.TournamentParticipants.Count < 2)
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.TournamentNeedsTwo"]);
            }

            tournament.Status = TournamentStatus.RegistrationClosed;

            await RejectPendings(tournament);

            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);

            await SaveAsync();

            await cacheService.RemoveAsync($"tournament:{id}");
            await cacheService.RemoveAsync($"bracket:{id}");
            await cacheService.RemoveAsync($"bracket:v3:{id}");
            await cacheService.RemoveAsync($"league_standings:{id}");

            // Discord-only announcement (no Expo push exists for closing registration).
            await this.tournamentNotifier.RegistrationClosed(tournament);
        }

        public async Task Publish(Guid id)
        {
            await OpenRegistration(id);
        }

        public async Task OpenRegistration(Guid id)
        {
            var tournament = await ChangeTournamentStatus(
                id,
                TournamentStatus.RegistrationOpen,
                ShouldOpenRegistration,
                this.LocalizationService["BusinessRule.OpenRegistrationWrongStatus"]
            );

            await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.RegistrationOpen);

            // Notify all hub followers about the opened registration (Expo push + Discord webhook).
            // Was missing entirely before — SaveEntity only fires this on tournament CREATION,
            // never on this explicit open-registration transition for an existing tournament.
            await this.tournamentNotifier.RegistrationOpened(tournament);
        }

        public async Task<TournamentOverview> GetOverview(Guid id)
        {
            string cacheKey = $"tournament:{id}";

            var cachedTournament = await cacheService.GetAsync<TournamentOverview>(cacheKey);
            if (cachedTournament != null)
            {
                return cachedTournament;
            }
            var data = await this.AppUnitOfWork.TournamentRepository.GetOverview(id);

            await cacheService.SetAsync(cacheKey, data, TimeSpan.FromMinutes(1));

            return data!;
        }

        /// <summary>
        /// v2 of the overview endpoint. Same payload as v1 plus <see cref="TournamentOverview.CanManage"/>
        /// so the client can surface owner-level controls to hub admins / platform admins as well.
        /// CanManage is computed per request and never cached (v1 omits it entirely).
        /// </summary>
        public async Task<TournamentOverview> GetOverviewV2(Guid id)
        {
            var data = await GetOverview(id);

            data.CanManage = await this.tournamentAuth.CanManageTournamentAsync(id);

            // Tell the client whether the caller passes the exclusivity gate so it can hide the
            // Join button for plain members. Short-circuits: no query for non-exclusive tournaments
            // or for managers (who always have access).
            data.HasExclusiveAccess = !data.IsExclusive
                || data.CanManage
                || await CallerHasExclusiveRole(data.HubId);

            return data;
        }

        /// <summary>
        /// v3 of the overview endpoint. Same payload as v2 plus <see cref="TournamentOverview.HasUserRegistered"/>
        /// so the mobile client can render the join / registered state without firing a second
        /// CHECK_REGISTRATION round-trip. Everything else is identical to v2 — CanManage is
        /// computed per request, HasExclusiveAccess is populated the same way.
        /// </summary>
        public async Task<TournamentOverview> GetOverviewV3(Guid id)
        {
            var data = await GetOverviewV2(id);

            var user = await this.UserContextReader.GetTokenUserInfoFromContext();
            if (user != null)
            {
                data.HasUserRegistered = await this.AppUnitOfWork.TournamentRepository
                    .CheckIsUserIsRegistered(id, user.UserId);
            }

            return data;
        }

        // Exclusive-or-higher role (Exclusive/Admin/Owner) in the given hub for the current caller.
        private async Task<bool> CallerHasExclusiveRole(Guid hubId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContext();
            if (user == null) return false;

            var role = await this.AppUnitOfWork.UserHubRepository.GetRole(user.UserId, hubId);
            return role == HubRole.HubOwner || role == HubRole.HubAdmin || role == HubRole.HubExclusive;
        }

        private async Task RejectPendings(TournamentEntity tournament)
        {
            foreach (var registration in tournament.TournamentRegistrations!)
            {
                registration.Status = TournamentRegistrationStatus.Rejected;
                await this.AppUnitOfWork.TournamentRegistrationRepository.UpdateEntity(registration, this.UserContextReader);
            }
        }

        protected override IRepository<TournamentEntity> GetRepository()
            => this.AppUnitOfWork.TournamentRepository;

        public async Task<bool> CheckIsUserRegistred(Guid id, Guid userId)
        {
            var isUserAlreadyRegistred = await this.AppUnitOfWork.TournamentRepository.CheckIsUserIsRegistered(id, userId);

            return isUserAlreadyRegistred;
        }

        public async Task SetRoundDeadline(Guid tournamentId, int roundNumber, DateTime? deadline, DateTime? roundStart, Guid? stageId = null, bool clearRoundStart = false, bool clearDeadline = false)
        {
            if (roundNumber < 1)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.RoundNumberPositive"]);

            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyStaffRoundDeadlines"]);

            // When a stage is given, scope the update to that bracket only — the Winners and Losers
            // brackets are separate stages that share RoundNumber, so a tournament-wide update would
            // leak the deadline across both. Null keeps the legacy tournament-wide behavior.
            var roundMatches = stageId.HasValue
                ? await this.AppUnitOfWork.MatchRepository.GetByStageAndRound(stageId.Value, roundNumber)
                : await this.AppUnitOfWork.MatchRepository.GetByTournamentAndRound(tournamentId, roundNumber);
            if (roundMatches.Count == 0)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.RoundNotFound"]);

            foreach (var match in roundMatches)
            {
                // Clear beats set: null RoundOpenAt = round open immediately, null RoundDeadline =
                // no deadline. A plain null value still means "keep existing" for old clients.
                if (clearRoundStart) match.RoundOpenAt = null;
                else if (roundStart != null) match.RoundOpenAt = roundStart;

                if (clearDeadline) match.RoundDeadline = null;
                else if (deadline != null) match.RoundDeadline = deadline;

                // The deadline changed (or vanished) — re-arm the reminder waves so a future
                // deadline gets fresh reminders instead of being seen as already-notified.
                if (clearDeadline || deadline != null) match.RoundReminderStage = 0;

                await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            }

            await this.SaveAsync();
            await cacheService.RemoveAsync($"bracket:{tournamentId}");
            await cacheService.RemoveAsync($"bracket:v3:{tournamentId}");
            await cacheService.RemoveAsync($"league_standings:{tournamentId}");
            await cacheService.RemoveAsync($"tournament:{tournamentId}");
        }

        /// <summary>
        /// Sets the series format for one round, the format sibling of <see cref="SetRoundDeadline"/>.
        /// </summary>
        /// <remarks>
        /// A match that already has a recorded game keeps the format it was played under — status
        /// alone is not the test, because a match can sit in Scheduled with games already reported
        /// (a level series awaiting its tiebreak), and re-formatting that mid-series would silently
        /// re-interpret the games already played. A pending result proposal counts as reported for
        /// the same reason: approving it must validate against the format it was submitted under.
        /// Skipped matches are counted, not refused, so changing a partly-played round still works
        /// for the fixtures it can legitimately touch.
        /// </remarks>
        public async Task<SetRoundBestOfResult> SetRoundBestOf(
            Guid tournamentId,
            int roundNumber,
            int? bestOf,
            int? tiebreakBestOf,
            Guid? stageId = null,
            bool clearBestOf = false)
        {
            if (roundNumber < 1)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.RoundNumberPositive"]);

            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyStaffRoundFormat"]);

            if (!clearBestOf)
            {
                if (bestOf == null)
                    throw new BusinessRuleException(this.LocalizationService["BusinessRule.ChooseRoundGames"]);
                if (bestOf < 1 || bestOf > SeriesEvaluator.MaxBestOf)
                    throw new BusinessRuleException(string.Format(this.LocalizationService["BusinessRule.BestOfRange"], SeriesEvaluator.MaxBestOf));
                if (tiebreakBestOf != null && (tiebreakBestOf < 1 || tiebreakBestOf > SeriesEvaluator.MaxBestOf))
                    throw new BusinessRuleException(string.Format(this.LocalizationService["BusinessRule.TiebreakBestOfRange"], SeriesEvaluator.MaxBestOf));
            }

            var roundMatches = stageId.HasValue
                ? await this.AppUnitOfWork.MatchRepository.GetByStageAndRound(stageId.Value, roundNumber)
                : await this.AppUnitOfWork.MatchRepository.GetByTournamentAndRound(tournamentId, roundNumber);
            if (roundMatches.Count == 0)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.RoundNotFound"]);

            var result = new SetRoundBestOfResult();

            foreach (var match in roundMatches)
            {
                if (!string.IsNullOrEmpty(match.GamesJson) || match.ProposedByUserId != null)
                {
                    result.SkippedLockedMatches++;
                    continue;
                }

                if (clearBestOf)
                {
                    match.BestOf = null;
                    match.TiebreakBestOf = null;
                }
                else
                {
                    match.BestOf = bestOf;
                    match.TiebreakBestOf = tiebreakBestOf;
                }

                result.UpdatedMatches++;
                await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            }

            if (result.UpdatedMatches == 0)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.RoundFormatLocked"]);

            await this.SaveAsync();
            await cacheService.RemoveAsync($"bracket:{tournamentId}");
            await cacheService.RemoveAsync($"bracket:v3:{tournamentId}");
            await cacheService.RemoveAsync($"league_standings:{tournamentId}");
            await cacheService.RemoveAsync($"tournament:{tournamentId}");

            return result;
        }

        public async Task CancelTournament(Guid id)
        {
            var tournament = await ChangeTournamentStatus(
                id,
                TournamentStatus.Cancelled,
                ShouldCancelTournament,
                this.LocalizationService["BusinessRule.CancelWrongStatus"]
            );

            await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentCanceled);
        }

        public async Task HardDeleteTournament(Guid id)
        {
            var tournament = await ChangeTournamentStatus(
                id,
                TournamentStatus.Deleted,
                ShouldDeleteTournament,
                this.LocalizationService["BusinessRule.DeleteWrongStatus"]
            );

            await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentDeleted);
        }

        private async Task<TournamentEntity> ChangeTournamentStatus(
            Guid id,
            TournamentStatus newStatus,
            Func<TournamentEntity, bool> validator,
            string errorMessage)
        {
            var tournament = await GetHubOwnedTournamentOrThrow(id);

            if (!validator(tournament))
                throw new BusinessRuleException(errorMessage);

            tournament.Status = newStatus;

            // Cancel and delete are both endings, so they start the same retention clock a natural
            // finish does. Only set if unset, matching the completion path: a tournament that is
            // cancelled after having been completed keeps the earlier, truer date.
            if (newStatus is TournamentStatus.Cancelled or TournamentStatus.Deleted or TournamentStatus.Completed)
            {
                tournament.EndedOn ??= DateTime.UtcNow;
            }

            await AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, UserContextReader);
            await SaveAsync();

            // A cancelled or deleted tournament has no result left to dispute, so its clips skip
            // the grace window entirely. After the save and never fatal: a storage failure must
            // not stop the tournament from being cancelled, and the periodic sweep is the retry.
            // The call absorbs and logs its own errors rather than throwing here.
            if (newStatus is TournamentStatus.Cancelled or TournamentStatus.Deleted)
            {
                await evidenceRetention.PurgeTournamentVideosAsync(id);
            }

            await InvalidateTournamentCache(id, tournament.HubId!.Value);

            return tournament;
        }

        // Hub-level counterpart of TournamentAuthorizationService.CanManageTournamentAsync, which
        // needs a tournament id we don't have when listing a hub. Anonymous callers get false.
        private async Task<bool> CanManageHubAsync(Guid hubId)
        {
            var caller = await this.UserContextReader.GetTokenUserInfoFromContext();
            if (caller == null) return false;

            if (caller.RoleEnum == UserRoleEnum.Admin) return true;

            var role = await this.userHubService.GetUserHubRoleCachedAsync(caller.UserId, hubId);
            return role == HubRole.HubOwner || role == HubRole.HubAdmin;
        }

        private async Task<TournamentEntity> GetHubOwnedTournamentOrThrow(Guid tournamentId)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithHubById(tournamentId);

            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId))
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyStaffManageTournament"]);
            }

            return tournament;
        }

        public override async Task<TournamentDto> SaveEntity(TournamentPost inputDto, bool doSave = true)
        {
            // Read before the save: whether this edit is the one switching the ready check ON.
            // Only an edit that sends true can be, so every other save skips the query.
            bool checkInSwitchedOn = false;
            if (inputDto.Id.HasValue && inputDto.RequireMatchCheckIn == true)
            {
                var before = await this.AppUnitOfWork.TournamentRepository.ShallowGetById(inputDto.Id.Value);
                checkInSwitchedOn = before != null && !before.RequireMatchCheckIn;
            }

            TournamentDto model = await this.ServiceFunctions.SaveEntity(
                this.GetRepository(),
                this,
                this.Validator,
                inputDto,
                this.GetEntityById,
                this.BeforeSave,
                this.BeforeDtoMapToEntity,
                doSave);

            if (inputDto.Id is null)
            {
                if (model.RegistrationOpensAt is null)
                {
                    await this.hubActivityService.LogActivity(model.HubId!.Value, model.Id!.Value, HubActivityType.RegistrationOpen);

                    // Notify all hub followers about the new tournament (Expo push + Discord webhook)
                    await this.tournamentNotifier.RegistrationOpened(model);
                }
                else
                {
                    // A scheduled tournament announces twice on purpose: "this is coming" now, and
                    // "you can sign up" when the sweep opens it. The "registration open" hub-activity
                    // entry is NOT written here — it belongs to the moment registration actually
                    // opens, which the sweep records.
                    await this.tournamentNotifier.RegistrationScheduled(model);
                }
            }
            else
            {
                // Turning the ready check on mid-tournament must only reach fixtures whose window
                // opens from now on. Anything already inside its window — or past kick-off — never
                // showed anyone a button, and the sweep rules back 24 hours: left alone, the next
                // tick forfeits or voids all of it. After the save, so a rejected edit exempts nothing.
                if (checkInSwitchedOn && model.RequireMatchCheckIn && model.Id.HasValue)
                {
                    var now = DateTime.UtcNow;
                    await this.AppUnitOfWork.MatchRepository.ExemptOpenCheckIns(
                        model.Id.Value, now.AddMinutes(GameHubz.DataModels.Consts.MatchCheckInRules.OpensBeforeMinutes), now);

                    await cacheService.RemoveByPatternAsync($"bracket:{model.Id}:*");
                    await cacheService.RemoveByPatternAsync($"bracket:v3:{model.Id}:*");
                }

                await cacheService.RemoveAsync($"tournament:{model.Id}");
                // Tournament-level settings (e.g. RequireResultApproval) are projected into the
                // bracket structure response, so flush the bracket cache too — otherwise the new
                // setting won't be visible until the 5-minute cache window expires.
                await cacheService.RemoveAsync($"bracket:{model.Id}");
                await cacheService.RemoveAsync($"bracket:v3:{model.Id}");
                await cacheService.RemoveAsync($"league_standings:{model.Id}");
            }

            // Wipe every paginated tournament list cached for this hub — the new (or edited)
            // tournament could appear on any page, not just page 0.
            await cacheService.RemoveByPatternAsync($"tournaments:hub:{inputDto.HubId}:*");
            await cacheService.RemoveAsync($"hub_overview:{model.HubId!.Value}");

            return model;
        }

        /// <summary>
        /// Solo-vs-team is locked at creation (flipping it would orphan team rosters or solo
        /// participants). Team size, win condition, exclusivity, country scope and double round-robin
        /// can still be edited, but only before the tournament starts and only by clients that opt in
        /// via <see cref="TournamentPost.AllowStructuralEdits"/>. Older clients leave the flag off and
        /// don't send these fields — without preservation, default bool/null on the DTO would silently
        /// flip a team tournament to solo (and unscope countries / clear exclusivity) on every edit.
        /// </summary>
        protected override async Task BeforeDtoMapToEntity(TournamentPost inputDto, bool isNew)
        {
            if (isNew || !inputDto.Id.HasValue) return;

            var existing = await this.AppUnitOfWork.TournamentRepository.ShallowGetById(inputDto.Id.Value);
            if (existing == null) return;

            inputDto.IsTeamTournament = existing.IsTeamTournament;

            // The series format stays editable for the whole life of a tournament, unlike the
            // structural fields below: a match freezes its own format the moment a result lands on
            // it, so a change here can only ever reach fixtures still to be played.
            //
            // Absence of BestOf — not AllowStructuralEdits — is what marks an old client here. The
            // client shipped before this feature already sets that flag, so gating on it would let
            // its edits (which carry no series fields at all) silently reset a Bo3 tournament to
            // Bo1. TiebreakBestOf, SeriesWinCondition and KnockoutBestOf always travel with
            // BestOf, so one check covers all four.
            if (inputDto.BestOf == null)
            {
                inputDto.BestOf = existing.BestOf;
                inputDto.SeriesWinCondition = existing.SeriesWinCondition;
                inputDto.TiebreakBestOf = existing.TiebreakBestOf;
                inputDto.KnockoutBestOf = existing.KnockoutBestOf;
            }

            // The ready check travels the same way, and for the same reason: a client that predates
            // it sends nothing, and a null read as "off" would silently disable the check on every
            // edit an older app makes. It stays editable for the whole life of a tournament — like
            // the series format, a change can only ever reach fixtures still to be played.
            if (inputDto.RequireMatchCheckIn == null)
            {
                inputDto.RequireMatchCheckIn = existing.RequireMatchCheckIn;
                inputDto.CheckInGraceMinutes = existing.CheckInGraceMinutes;
            }

            // The scheduled opening travels under its own opt-in flag rather than
            // AllowStructuralEdits: the currently shipped client already sets that flag and knows
            // nothing about this field, so folding the two together would let its edits null the
            // schedule — and the next sweep would never open the tournament at all.
            // Rescheduling only makes sense while the tournament is still waiting to open; once
            // registration is live the stored value is history, and re-arming it would hide a
            // tournament people have already joined.
            bool canEditSchedule = inputDto.AllowScheduleEdits && existing.Status == TournamentStatus.Draft;

            if (!canEditSchedule)
            {
                inputDto.RegistrationOpensAt = existing.RegistrationOpensAt;
            }
            else if (inputDto.RegistrationOpensAt is null && existing.RegistrationOpensAt is not null)
            {
                // Dropping the schedule would leave a Draft nothing ever opens. Opening it now is a
                // lifecycle transition with its own announcement — that is what OpenRegistration is.
                throw new BusinessRuleException(
                    this.LocalizationService["BusinessRule.PickNewOpeningTime"]);
            }

            bool canEditStructural = inputDto.AllowStructuralEdits
                && existing.Status < TournamentStatus.InProgress;
            if (canEditStructural) return;

            inputDto.TeamSize = existing.TeamSize;
            inputDto.TeamWinCondition = existing.TeamWinCondition;
            inputDto.AllowReserves = existing.AllowReserves;
            inputDto.MaxReserves = existing.MaxReserves;
            inputDto.IsExclusive = existing.IsExclusive;
            inputDto.Countries = existing.Countries == null ? null : new List<string>(existing.Countries);
            inputDto.DoubleRoundRobin = existing.DoubleRoundRobin;
            // Newer field that old clients don't send — without this, their edits would null it out
            // and silently downgrade a double-elimination knockout back to single.
            inputDto.KnockoutEliminationType = existing.KnockoutEliminationType;
        }

        /// <summary>
        /// When a tournament is created/edited with one or more countries, it becomes country-scoped
        /// and its Region is derived from the first country (country dictates region). An empty/null
        /// list leaves the tournament region-scoped using the explicitly chosen Region. Stored as
        /// canonical, de-duplicated ISO codes (null when none — never an empty array).
        /// </summary>
        protected override async Task BeforeSave(TournamentEntity entity, TournamentPost inputDto, bool isNew)
        {
            // F104: the generic POST /api/Tournament had no authorization, so any user could create a
            // tournament under any hub or edit any existing tournament. Creating one requires managing
            // the target hub; editing requires managing the existing tournament.
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            if (isNew)
            {
                if (!entity.HubId.HasValue)
                {
                    throw new BusinessRuleException(this.LocalizationService["BusinessRule.TournamentNeedsHub"]);
                }

                if (caller.RoleEnum != UserRoleEnum.Admin)
                {
                    await this.userHubService.EnsureCallerCanManage(entity.HubId.Value, caller.UserId);
                }
            }
            else if (entity.Id.HasValue)
            {
                if (!await this.tournamentAuth.CanManageTournamentAsync(entity.Id.Value))
                {
                    throw new UnauthorizedAccessToServiceException(this.LocalizationService);
                }
            }

            if (isNew && entity.RegistrationOpensAt.HasValue && entity.RegistrationOpensAt.Value <= DateTime.UtcNow)
            {
                // A moment already gone is not a schedule. Dropping it rather than rejecting keeps a
                // slow form submit (or a clock a few seconds off) from stranding the tournament in
                // Draft with nobody able to join — it just means "open now", the old behaviour.
                entity.RegistrationOpensAt = null;
            }

            if (entity.RegistrationOpensAt.HasValue)
            {
                if (entity.RegistrationDeadline.HasValue && entity.RegistrationOpensAt.Value >= entity.RegistrationDeadline.Value)
                {
                    throw new BusinessRuleException(this.LocalizationService["BusinessRule.RegistrationOpenBeforeDeadline"]);
                }

                if (entity.StartDate.HasValue && entity.RegistrationOpensAt.Value >= entity.StartDate.Value)
                {
                    throw new BusinessRuleException(this.LocalizationService["BusinessRule.RegistrationOpenBeforeStart"]);
                }

                if (isNew)
                {
                    // Status is the gate every registration path already checks (solo, team, and the
                    // feed's AvailableToJoin filter), so a scheduled tournament is simply created as a
                    // Draft and the background sweep publishes it. Forced here instead of trusted from
                    // the payload — otherwise a client could post a scheduled tournament that is
                    // already open, which is the one state this feature exists to prevent.
                    entity.Status = TournamentStatus.Draft;
                }
            }

            // Clamp rather than reject: Best-of arrives from a picker with a fixed set of options,
            // so an out-of-range value is a malformed client, not a user mistake worth a 400.
            // A create that omits it maps to 0, which normalizes to the Bo1 default.
            entity.BestOf = SeriesEvaluator.Normalize(entity.BestOf);
            if (entity.TiebreakBestOf != null)
                entity.TiebreakBestOf = SeriesEvaluator.Normalize(entity.TiebreakBestOf);

            // A knockout length only means something where a bracket follows another phase; on a
            // plain bracket or a league it would be a second number describing the same matches,
            // so it is dropped rather than stored to confuse a later read.
            entity.KnockoutBestOf = SeriesEvaluator.PlaysKnockoutAfterAnotherPhase(entity.Format) && entity.KnockoutBestOf != null
                ? SeriesEvaluator.Normalize(entity.KnockoutBestOf)
                : null;

            var codes = inputDto.Countries?
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c =>
                {
                    var country = CountryCatalog.Get(c)
                        ?? throw new BusinessRuleException(string.Format(this.LocalizationService["BusinessRule.UnknownCountryCode"], c));
                    return country.Code;
                })
                .Distinct()
                .ToList();

            if (codes is null || codes.Count == 0)
            {
                entity.Countries = null;
            }
            else
            {
                entity.Countries = codes;
                // Region is cosmetic for country-scoped tournaments (filtering uses Countries);
                // derive it from the first country so the displayed region stays sensible.
                entity.Region = CountryCatalog.Get(codes[0])!.Region;
            }

            await Task.CompletedTask;
        }

        // Draft belongs here too: a tournament scheduled to open tomorrow is the easiest one of all
        // to have created by mistake, and without this its organiser could not delete it until the
        // sweep opened it first.
        private static bool ShouldDeleteTournament(TournamentEntity tournament)
        {
            return tournament.Status == TournamentStatus.RegistrationClosed
                || tournament.Status == TournamentStatus.RegistrationOpen
                || tournament.Status == TournamentStatus.Draft;
        }

        private static bool ShouldCancelTournament(TournamentEntity tournament)
        {
            return tournament.Status == TournamentStatus.InProgress;
        }

        // Draft joined RegistrationClosed here with scheduled openings: a tournament waiting for its
        // opening time is exactly the case where an organiser needs an "open it now" override, and
        // this transition already carries the announcement the sweep would otherwise have fired.
        private static bool ShouldOpenRegistration(TournamentEntity tournament)
        {
            return tournament.Status == TournamentStatus.RegistrationClosed
                || tournament.Status == TournamentStatus.Draft;
        }

        private async Task InvalidateTournamentCache(Guid tournamentId, Guid hubId)
        {
            await cacheService.RemoveAsync($"bracket:{tournamentId}");
            await cacheService.RemoveAsync($"bracket:v3:{tournamentId}");
            await cacheService.RemoveAsync($"league_standings:{tournamentId}");
            await cacheService.RemoveAsync($"tournament:{tournamentId}");
            await cacheService.RemoveAsync($"hub_overview:{hubId}");
            // Wipes every cached page of every status for this hub — replaces the old
            // p:0/p:1 hand-listed invalidation that left page 2+ stale.
            await cacheService.RemoveByPatternAsync($"tournaments:hub:{hubId}:*");
        }

        private async Task UpdateRoundSchedule(Guid tournamentId, int roundNumber, DateTime? opensAt = null, DateTime? deadline = null)
        {
            if (roundNumber < 1)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.RoundNumberPositive"]);

            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyStaffRoundDeadlines"]);

            var roundMatches = await this.AppUnitOfWork.MatchRepository.GetByTournamentAndRound(tournamentId, roundNumber);
            if (roundMatches.Count == 0)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.RoundNotFound"]);

            foreach (var match in roundMatches)
            {
                if (opensAt != null) match.RoundOpenAt = opensAt;
                if (deadline != null) match.RoundDeadline = deadline;
                await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            }

            await this.SaveAsync();
            await cacheService.RemoveAsync($"bracket:{tournamentId}");
            await cacheService.RemoveAsync($"bracket:v3:{tournamentId}");
            await cacheService.RemoveAsync($"league_standings:{tournamentId}");
            await cacheService.RemoveAsync($"tournament:{tournamentId}");
        }
    }
}