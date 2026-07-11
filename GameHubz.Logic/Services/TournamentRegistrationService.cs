using FluentValidation;
using GameHubz.DataModels.Enums;
using System.Runtime.InteropServices;

namespace GameHubz.Logic.Services
{
    public class TournamentRegistrationService : AppBaseServiceGeneric<TournamentRegistrationEntity, TournamentRegistrationDto, TournamentRegistrationPost, TournamentRegistrationEdit>
    {
        private readonly TournamentParticipantService tournamentParticipantService;
        private readonly ICacheService cacheService;
        private readonly BadgeService badgeService;
        private readonly TournamentAuthorizationService tournamentAuth;

        public TournamentRegistrationService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<TournamentRegistrationEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader,
            TournamentParticipantService tournamentParticipantService,
            ICacheService cacheService,
            BadgeService badgeService,
            TournamentAuthorizationService tournamentAuth) : base(
                factory.CreateAppUnitOfWork(),
                userContextReader,
                localizationService,
                searchService,
                validator,
                mapper,
                serviceFunctions)
        {
            this.tournamentParticipantService = tournamentParticipantService;
            this.cacheService = cacheService;
            this.badgeService = badgeService;
            this.tournamentAuth = tournamentAuth;
        }

        /// <summary>
        /// Creates the registration through the generic pipeline, then bumps the hub managers'
        /// "pending registrations" badge. Badge-only by design — no push. Both solo registration
        /// (generic POST) and <see cref="RegisterTeam"/> funnel through here.
        /// </summary>
        public override async Task<TournamentRegistrationDto> SaveEntity(TournamentRegistrationPost inputDto, bool doSave = true)
        {
            var dto = await base.SaveEntity(inputDto, doSave);

            // New rows are always Pending (set in BeforeDtoMapToEntity). Edits keep their status;
            // bumping on a Pending row is the only case the managers' queue cares about.
            if (doSave && inputDto.TournamentId.HasValue && inputDto.Status == TournamentRegistrationStatus.Pending)
                await this.badgeService.PushToTournamentManagersAsync(inputDto.TournamentId.Value);

            return dto;
        }

        protected override async Task BeforeDtoMapToEntity(TournamentRegistrationPost inputDto, bool isNew)
        {
            inputDto.Status = TournamentRegistrationStatus.Pending;

            await Task.CompletedTask;
        }

        /// <summary>
        /// Enforces region/country eligibility for solo registrations so the loophole of reaching a
        /// tournament via the hub (which doesn't apply the feed's visibility filter) can't be used to
        /// join a tournament the user isn't eligible for. Team registrations have no single country,
        /// so the country gate is skipped for them.
        /// </summary>
        protected override async Task BeforeSave(TournamentRegistrationEntity entity, TournamentRegistrationPost inputDto, bool isNew)
        {
            // F37: a solo registration is always for the authenticated caller. Never trust a body
            // UserId — otherwise a user could register (and have eligibility evaluated as) someone else.
            // Team registrations carry no UserId and go through the captain-checked RegisterTeam path.
            if (isNew && !entity.TeamId.HasValue)
            {
                var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
                entity.UserId = caller.UserId;
            }

            if (isNew && entity.TournamentId.HasValue && (entity.UserId.HasValue || entity.TeamId.HasValue))
            {
                // F41: registrations are only accepted while the tournament's registration is open.
                var tournamentForStatus = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(entity.TournamentId.Value);
                if (tournamentForStatus.Status != TournamentStatus.RegistrationOpen)
                {
                    throw new BusinessRuleException("Registration is not open for this tournament.");
                }

                // Block duplicate sign-ups. An entrant that already has a non-rejected registration,
                // or is already a confirmed participant, must not be able to create a second row.
                // Repeated rows were the root cause of duplicate participants that later broke the
                // players list and couldn't be cleanly removed.
                bool alreadyRegistered = await this.AppUnitOfWork.TournamentRegistrationRepository
                    .ExistsNonRejected(entity.TournamentId.Value, entity.UserId, entity.TeamId);

                bool alreadyParticipant = entity.TeamId.HasValue
                    ? await this.AppUnitOfWork.TournamentParticipantRepository.ExistsForTeam(entity.TournamentId.Value, entity.TeamId.Value)
                    : await this.AppUnitOfWork.TournamentParticipantRepository.ExistsForUser(entity.TournamentId.Value, entity.UserId!.Value);

                if (alreadyRegistered || alreadyParticipant)
                {
                    throw new BusinessRuleException("You're already registered for this tournament.");
                }
            }

            if (isNew && entity.UserId.HasValue && entity.TournamentId.HasValue)
            {
                var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(entity.TournamentId.Value);
                var user = await this.AppUnitOfWork.UserRepository.ShallowGetByIdOrThrowIfNull(entity.UserId.Value);

                if (!IsEligibleToJoin(tournament, user))
                {
                    throw new BusinessRuleException("You can't join this tournament — it's restricted to a different region or country.");
                }

                // Exclusive tournaments require an Exclusive-or-higher role in the owning hub.
                if (tournament.IsExclusive && tournament.HubId.HasValue)
                {
                    var role = await this.AppUnitOfWork.UserHubRepository.GetRole(entity.UserId.Value, tournament.HubId.Value);
                    bool hasExclusiveAccess = role == HubRole.HubOwner
                        || role == HubRole.HubAdmin
                        || role == HubRole.HubExclusive;

                    if (!hasExclusiveAccess)
                    {
                        throw new BusinessRuleException("You can't join this tournament — it's restricted to exclusive members of this hub.");
                    }
                }
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Mirrors the tournament-feed visibility rules: country-scoped tournaments require the user's
        /// country to be in the list; region-scoped tournaments require the user's region (or GLOBAL).
        /// </summary>
        private static bool IsEligibleToJoin(TournamentEntity tournament, UserEntity user)
        {
            if (tournament.Countries != null && tournament.Countries.Count > 0)
            {
                return !string.IsNullOrEmpty(user.Country) && tournament.Countries.Contains(user.Country!);
            }

            return tournament.Region == RegionType.GLOBAL || tournament.Region == user.Region;
        }

        public async Task ApproveRegistration(Guid registrationId)
        {
            var tournamentRegistration = await this.AppUnitOfWork.TournamentRegistrationRepository.GetWithTournament(registrationId);

            // F35: approving a registration injects a participant into the roster — managers only.
            await this.EnsureCanManageTournament(tournamentRegistration.TournamentId);

            // If this entrant is already a participant (e.g. a stale/duplicate registration row, or
            // this row was approved before), only flip the status — creating a second participant is
            // exactly the bug that produced duplicate players, and it shouldn't count against capacity.
            bool isTeamRegistration = tournamentRegistration.TeamId.HasValue;
            bool alreadyParticipant = tournamentRegistration.Tournament?.TournamentParticipants?.Any(p =>
                isTeamRegistration
                    ? p.TeamId == tournamentRegistration.TeamId
                    : p.UserId == tournamentRegistration.UserId) ?? false;

            if (!alreadyParticipant && IsAlreadyFullTournament(tournamentRegistration, 1))
            {
                throw new BusinessRuleException("Cannot approve registration. Tournament has reached maximum number of players.");
            }

            await SetRegistrationStatus(tournamentRegistration, TournamentRegistrationStatus.Approved);

            if (!alreadyParticipant)
            {
                await CreateTournamentParticipant(tournamentRegistration);
            }

            await this.SaveAsync();

            if (tournamentRegistration.UserId.HasValue)
            {
                // Approving registration affects this user's feed across every status / page.
                await cacheService.RemoveByPatternAsync($"user_feed:{tournamentRegistration.UserId}:*");
                await cacheService.RemoveAsync($"tournament:{tournamentRegistration.TournamentId}");
                await cacheService.RemoveAsync($"bracket:{tournamentRegistration.TournamentId}");
                await cacheService.RemoveAsync($"bracket:v3:{tournamentRegistration.TournamentId}");
                await cacheService.RemoveAsync($"league_standings:{tournamentRegistration.TournamentId}");
            }
            // Post-commit invalidation of the participants list — BeforeSave in the participant
            // service runs before the DB commit, so this catches the rare race where a concurrent
            // read between BeforeSave and SaveAsync could re-cache the stale list.
            await cacheService.RemoveAsync($"tournament_participants:{tournamentRegistration.TournamentId}");

            // The pending-registrations queue shrank — refresh the managers' badge.
            await this.badgeService.PushToTournamentManagersAsync(tournamentRegistration.TournamentId!.Value);
        }

        public async Task ApproveRegistrations(List<Guid> registrationId)
        {
            List<TournamentRegistrationEntity> tournamentRegistration = await this.AppUnitOfWork.TournamentRegistrationRepository.GetByIds(registrationId);

            if (tournamentRegistration.Count == 0)
            {
                return;
            }

            // F35: bulk approval is a manager action.
            await this.EnsureCanManageTournament(tournamentRegistration.First().TournamentId);

            var tournament = tournamentRegistration.First().Tournament;

            // Teams / solo users that are already participants of this tournament. Re-approving a
            // stale pending row for one of them must neither create a second participant nor count
            // against the capacity check below.
            var existingTeamIds = new HashSet<Guid>();
            var existingUserIds = new HashSet<Guid>();
            if (tournament?.TournamentParticipants != null)
            {
                foreach (var participant in tournament.TournamentParticipants)
                {
                    if (participant.TeamId.HasValue) existingTeamIds.Add(participant.TeamId.Value);
                    else if (participant.UserId.HasValue) existingUserIds.Add(participant.UserId.Value);
                }
            }

            // A team can have more than one pending registration row, and a row can belong to a
            // team/user that is already in. We mark every selected row as approved, but materialize
            // exactly one participant per genuinely-new team or solo user. This both avoids attaching
            // the same TournamentTeamEntity twice to the shared DbContext (EF tracking conflict) and
            // gives an accurate incoming-participant count for the capacity check, instead of the raw
            // batch size which over-counts duplicate-team rows.
            var newTeamIds = new HashSet<Guid>();
            var newUserIds = new HashSet<Guid>();
            var registrationsToMaterialize = new List<TournamentRegistrationEntity>();

            foreach (var registration in tournamentRegistration)
            {
                if (registration.TeamId.HasValue)
                {
                    if (!existingTeamIds.Contains(registration.TeamId.Value) && newTeamIds.Add(registration.TeamId.Value))
                    {
                        registrationsToMaterialize.Add(registration);
                    }
                }
                else if (registration.UserId.HasValue)
                {
                    if (!existingUserIds.Contains(registration.UserId.Value) && newUserIds.Add(registration.UserId.Value))
                    {
                        registrationsToMaterialize.Add(registration);
                    }
                }
                else
                {
                    // A registration is normally either team- or user-bound; if somehow neither,
                    // fall back to the original behavior and still create a participant for it.
                    registrationsToMaterialize.Add(registration);
                }
            }

            if (IsAlreadyFullTournament(tournamentRegistration.First(), registrationsToMaterialize.Count))
            {
                throw new BusinessRuleException("Cannot approve registration. Tournament has reached maximum number of players.");
            }

            foreach (var registration in tournamentRegistration)
            {
                await SetRegistrationStatus(registration, TournamentRegistrationStatus.Approved);
            }

            foreach (var registration in registrationsToMaterialize)
            {
                await CreateTournamentParticipant(registration);
            }

            await this.SaveAsync();

            foreach (var registration in tournamentRegistration)
            {
                if (registration.UserId.HasValue)
                {
                    await cacheService.RemoveAsync($"player_stats:{registration.UserId}");
                    // Approval changes which feeds this user sees the tournament under.
                    await cacheService.RemoveByPatternAsync($"user_feed:{registration.UserId}:*");
                }
            }

            await cacheService.RemoveAsync($"tournament:{tournamentRegistration.First().TournamentId}");
            await cacheService.RemoveAsync($"bracket:{tournamentRegistration.First().TournamentId}");
            await cacheService.RemoveAsync($"bracket:v3:{tournamentRegistration.First().TournamentId}");
            await cacheService.RemoveAsync($"league_standings:{tournamentRegistration.First().TournamentId}");
            // Post-commit safety net — see ApproveRegistration above.
            await cacheService.RemoveAsync($"tournament_participants:{tournamentRegistration.First().TournamentId}");

            // The pending-registrations queue shrank — refresh the managers' badge.
            await this.badgeService.PushToTournamentManagersAsync(tournamentRegistration.First().TournamentId!.Value);
        }

        public async Task RejectRegistration(Guid registrationId)
        {
            var tournamentRegistration = await this.AppUnitOfWork.TournamentRegistrationRepository.ShallowGetByIdOrThrowIfNull(registrationId);

            // F35: rejecting a registration is a manager action.
            await this.EnsureCanManageTournament(tournamentRegistration.TournamentId);

            await SetRegistrationStatus(tournamentRegistration, TournamentRegistrationStatus.Rejected);

            await this.SaveAsync();

            await cacheService.RemoveAsync($"tournament:{tournamentRegistration.TournamentId}");

            // The pending-registrations queue shrank — refresh the managers' badge.
            await this.badgeService.PushToTournamentManagersAsync(tournamentRegistration.TournamentId!.Value);
        }

        public async Task<List<TournamentRegistrationOverview>> GetPendingByTournamentId(Guid tournamentId)
        {
            var registrations = await this.AppUnitOfWork.TournamentRegistrationRepository.GetPendingByTournamenId(tournamentId);

            return registrations;
        }

        public async Task RegisterTeam(Guid tournamentId, Guid teamId)
        {
            // F35/F37: registering a team is a participant action performed by the team captain — not a
            // manager action and not something a user may do for a team they don't own. Verify the
            // caller is the captain of this team before creating the registration.
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var team = await this.AppUnitOfWork.TournamentTeamRepository.ShallowGetByIdOrThrowIfNull(teamId);
            if (team.CaptainUserId != caller.UserId)
            {
                throw new UnauthorizedAccessToServiceException(this.LocalizationService);
            }

            // Idempotent: this is exposed as a GET, which HTTP clients (OkHttp, etc.) freely retry
            // on a flaky connection. If the first call already created the registration but the
            // response was lost, the retry would otherwise hit BeforeSave's duplicate guard and
            // surface a confusing "already registered" error for something that actually succeeded.
            // Treat an existing non-rejected registration / participant as success instead.
            bool alreadyRegistered = await this.AppUnitOfWork.TournamentRegistrationRepository
                .ExistsNonRejected(tournamentId, null, teamId);
            bool alreadyParticipant = await this.AppUnitOfWork.TournamentParticipantRepository
                .ExistsForTeam(tournamentId, teamId);

            if (alreadyRegistered || alreadyParticipant)
                return;

            var tournamentRegistrationPost = new TournamentRegistrationPost
            {
                Status = TournamentRegistrationStatus.Pending,
                TeamId = teamId,
                TournamentId = tournamentId,
                UserId = null
            };

            await this.SaveEntity(tournamentRegistrationPost);
        }

        private async Task CreateTournamentParticipant(TournamentRegistrationEntity tournamentRegistration)
        {
            try
            {
                if (tournamentRegistration.TeamId.HasValue)
                {
                    // Team tournament: create participant linked to team
                    var tournamentParticipants = new TournamentParticipantPost
                    {
                        TournamentId = tournamentRegistration.TournamentId,
                        UserId = tournamentRegistration.UserId,
                        TeamId = tournamentRegistration.TeamId
                    };

                    var dto = await this.tournamentParticipantService.SaveEntity(tournamentParticipants);

                    // Link the team to the participant
                    var team = await this.AppUnitOfWork.TournamentTeamRepository.ShallowGetByIdOrThrowIfNull(tournamentRegistration.TeamId.Value);
                    team.TournamentParticipantId = dto.Id;
                    await this.AppUnitOfWork.TournamentTeamRepository.UpdateEntity(team, this.UserContextReader);
                }
                else
                {
                    // Solo tournament: existing behavior
                    var tournamentParticipants = new TournamentParticipantPost
                    {
                        TournamentId = tournamentRegistration.TournamentId,
                        UserId = tournamentRegistration.UserId
                    };

                    await this.tournamentParticipantService.SaveEntity(tournamentParticipants);
                }
            }
            catch (Exception ex) when (IsDuplicateParticipantViolation(ex))
            {
                // The unique index on TournamentParticipant rejected this insert because a
                // concurrent approval (e.g. two admins approving at the same moment) already
                // created the participant. The entrant is in — the desired end state — so surface
                // a clear message instead of a raw DB error. The in-memory "already a participant"
                // guard can't catch this; only the DB sees the other transaction's pending row.
                throw new BusinessRuleException("This registration has already been approved.");
            }
        }

        // Detects a Postgres unique-violation (SqlState 23505) anywhere in the exception chain
        // without taking a compile-time dependency on Npgsql in this layer.
        private static bool IsDuplicateParticipantViolation(Exception ex)
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

        private async Task SetRegistrationStatus(TournamentRegistrationEntity tournamentRegistration, TournamentRegistrationStatus status)
        {
            tournamentRegistration.Status = status;

            await this.AppUnitOfWork.TournamentRegistrationRepository.UpdateEntity(tournamentRegistration, this.UserContextReader);
        }

        private static bool IsAlreadyFullTournament(TournamentRegistrationEntity tournamentRegistration, int numberOfNewParticipient)
        {
            return tournamentRegistration.Tournament != null
                && tournamentRegistration.Tournament.TournamentParticipants != null
                && tournamentRegistration.Tournament!.MaxPlayers < (tournamentRegistration.Tournament.TournamentParticipants.Count + numberOfNewParticipient);
        }

        protected override IRepository<TournamentRegistrationEntity> GetRepository()
            => this.AppUnitOfWork.TournamentRegistrationRepository;

        private async Task EnsureCanManageTournament(Guid? tournamentId)
        {
            if (!tournamentId.HasValue || !await this.tournamentAuth.CanManageTournamentAsync(tournamentId.Value))
            {
                throw new UnauthorizedAccessToServiceException(this.LocalizationService);
            }
        }
    }
}