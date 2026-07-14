using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AutoMapper;
using FluentValidation;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

using GameHubz.Common.Interfaces;
using GameHubz.Common.Models;
using GameHubz.Data.Context;
using GameHubz.DataModels.Config;
using GameHubz.DataModels.Consts;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;
using GameHubz.Logic.SignalR;
using GameHubz.Logic.Test.Factories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GameHubz.Logic.Test.Bracket
{
    /// <summary>
    /// Builds a real <see cref="BracketService"/> wired over an EF Core database. Two backends:
    /// <list type="bullet">
    /// <item>In-memory (default) — fast, used for generation + structure-read tests.</item>
    /// <item>SQLite in-memory (<c>useSqlite: true</c>) — a real relational provider, needed for the
    /// result/advancement path because it calls <c>pg_advisory_lock</c> via raw SQL (relational-only)
    /// and uses relational query features. The Postgres advisory-lock functions are registered as
    /// no-ops on the connection so the production locking code runs unchanged.</item>
    /// </list>
    /// Auxiliary collaborators (cache, notifications) are lightweight fakes; the authorization service
    /// is real but never reaches its UserHubService because the test token is an Admin, which
    /// short-circuits CanManageTournamentAsync.
    ///
    /// Each call to <see cref="NewService"/> gets a fresh context (clean change-tracker) over the same
    /// store, mirroring a new request scope — call once per logical operation when chaining
    /// generate -> report -> revert. Reads for assertions go through <see cref="ReadContext"/>.
    /// </summary>
    internal sealed class BracketTestHarness
    {
        private readonly Func<ApplicationContext> newContext;
        private readonly SqliteConnection? sqliteConnection;
        private readonly ILocalizationService localization;
        private readonly IMapper mapper;
        private readonly SearchService searchService;
        private readonly ServiceFunctions serviceFunctions;
        private BracketService? lazyService;

        public FakeCacheService Cache { get; } = new();
        public FakeNotificationService Notifications { get; } = new();
        public IUserContextReader UserContext { get; }

        /// <summary>The Admin/owner user id the mocked token resolves to.</summary>
        public static Guid OwnerUserId => Guid.Parse(Consts.TestUserId);

        public BracketTestHarness(bool useSqlite = false)
        {
            localization = new LocalizationServiceFactory().CreateService();
            UserContext = new UserContextReaderFactory().CreateService();
            mapper = new MapperFactory().CreateService();
            searchService = new SearchServiceFactory().CreateService();
            serviceFunctions = new ServiceFunctionsFactory().CreateService();

            if (useSqlite)
            {
                // Foreign Keys=False: these tests exercise bracket math, not referential integrity.
                // Disabling FK enforcement keeps seeding identical to the in-memory provider (no need to
                // seed the whole User graph behind every participant/hub-owner FK).
                sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
                sqliteConnection.Open();

                // Make the Postgres advisory-lock calls in TournamentRepository.AcquireAdvancementLock
                // resolve to harmless no-ops instead of "no such function".
                sqliteConnection.CreateFunction("pg_advisory_lock", (long key) => 1L);
                sqliteConnection.CreateFunction("pg_advisory_unlock", (long key) => 1L);

                var sqliteOptions = new DbContextOptionsBuilder<TestApplicationContext>()
                    .UseSqlite(sqliteConnection)
                    .Options;

                newContext = () => new TestApplicationContext(sqliteOptions);

                using var init = newContext();
                init.Database.EnsureCreated();
            }
            else
            {
                var inMemoryOptions = new DbContextOptionsBuilder<ApplicationContext>()
                    .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                    .Options;

                newContext = () => new ApplicationContext(inMemoryOptions);
            }
        }

        /// <summary>
        /// A BracketService over a FRESH context (clean change-tracker) that shares the same store,
        /// cache and notification sink. Mirrors a new request scope in production — call once per
        /// logical operation when chaining generate -> report -> revert, so accumulated tracked
        /// entities from one step don't collide with the next. Uses the default Admin token.
        /// </summary>
        public BracketService NewService() => BuildService(UserContext);

        /// <summary>
        /// A BracketService acting as a specific (typically non-privileged) user — used to exercise
        /// the result-approval / proposal path. NOTE: for a non-Admin, non-owner caller you must also
        /// pre-seed the authz cache (see <see cref="DenyManageFor"/>) so CanManageTournamentAsync
        /// resolves to false without dereferencing the (null) UserHubService.
        /// </summary>
        public BracketService NewServiceAsUser(Guid userId, string role = "User")
            => BuildService(BuildReader(userId, role));

        private BracketService BuildService(IUserContextReader userContext)
        {
            var factory = new TestUnitOfWorkFactory(newContext(), localization);

            var hubActivityService = new HubActivityService(
                factory, mapper, localization, new Mock<IValidator<HubActivityEntity>>().Object,
                searchService, serviceFunctions, userContext, Cache);

            // userHubService is null!: the Admin token short-circuits CanManageTournamentAsync, and
            // for non-Admin callers the test pre-seeds the authz cache so the null is never reached.
            var tournamentAuth = new TournamentAuthorizationService(
                factory, userContext, localization, Cache, userHubService: null!);

            // PushAsync is best-effort (try/catch), so a bare mocked SignalR hub context is enough —
            // the badge send no-ops while the badge computation still exercises the real repositories.
            // The scope factory backs the fire-and-forget manager fan-out; the bare mock makes that
            // background task no-op inside its own catch, which is what tests want.
            var badgeService = new BadgeService(
                factory, userContext, localization,
                new Mock<IHubContext<UserHub>>().Object,
                new Mock<IServiceScopeFactory>().Object);

            // Real notifiers over the shared store: test hubs never carry a DiscordWebhookUrl, so the
            // Discord branch resolves to "not configured" and only the mocked transport would be hit.
            var embedBuilder = new DiscordEmbedBuilder();
            var discordNotifications = new Mock<IDiscordNotificationService>().Object;
            var tournamentNotifier = new TournamentNotifier(factory, Notifications, discordNotifications, embedBuilder);
            var matchNotifier = new MatchNotifier(factory, discordNotifications, embedBuilder);
            var bracketNotifier = new BracketNotifier(factory, Notifications, discordNotifications, embedBuilder);

            // Personal bot DMs (phase 2): mocked transport — test users never link Discord, so the
            // DM branches no-op before ever reaching it.
            var discordDm = new Mock<IDiscordDmService>().Object;
            var shareLinks = Options.Create(new ShareLinksConfig());

            return new BracketService(
                factory, userContext, localization,
                hubActivityService, Cache, Notifications, tournamentAuth, badgeService,
                tournamentNotifier, matchNotifier, bracketNotifier,
                discordDm, shareLinks);
        }

        /// <summary>
        /// TeamMatchService acting as a specific user — drives the tie-break representative flow.
        /// Same fresh-context-per-operation rule as NewService.
        /// </summary>
        public TeamMatchService NewTeamMatchServiceAsUser(Guid userId)
            => new TeamMatchService(
                new TestUnitOfWorkFactory(newContext(), localization),
                BuildReader(userId, "User"),
                localization,
                Cache);

        private static IUserContextReader BuildReader(Guid userId, string role)
        {
            var token = new TokenUserInfo { UserId = userId, Role = role };
            var reader = new Mock<IUserContextReader>();
            reader.Setup(x => x.GetTokenUserInfoFromContext()).Returns(Task.FromResult<TokenUserInfo?>(token));
            reader.Setup(x => x.GetTokenUserInfoFromContextThrowIfNull()).Returns(Task.FromResult(token));
            reader.Setup(x => x.GetRequestData())
                .Returns(Task.FromResult(new UserRequestData(token, Languages.Serbian)));
            return reader.Object;
        }

        /// <summary>
        /// Pre-seeds the authorization cache so <paramref name="userId"/> is treated as NOT a tournament
        /// manager — lets the proposal path run for a participant without a real UserHubService.
        /// </summary>
        public Task DenyManageFor(Guid userId, Guid tournamentId)
            => Cache.SetAsync<bool?>($"tournament_authz:{userId}:{tournamentId}", false);

        /// <summary>Convenience single-operation service (generation / single-read tests). Built once.</summary>
        public BracketService Service => lazyService ??= NewService();

        public ApplicationContext ReadContext() => newContext();

        /// <summary>Mirror of the generator's bracket-size rule, for computing expected counts.</summary>
        public static int NextPowerOfTwo(int n)
        {
            int power = 1;
            while (power < n) power <<= 1;
            return power;
        }

        // ---- Seeding -------------------------------------------------------

        /// <summary>
        /// Seeds a hub-owned tournament with <paramref name="participantCount"/> participants
        /// (Seed = 1..N in creation order). Returns the tournament id.
        ///
        /// Seeding goes through a dedicated context (not the service's), so the service loads
        /// the data fresh on its own context — exactly as it would against a real database. Sharing
        /// the service's context here would leave the seeded rows tracked and make the generators'
        /// UpdateEntity calls throw an identity conflict.
        /// </summary>
        public Task<Guid> SeedSoloTournamentAsync(
            TournamentFormat format,
            int participantCount,
            bool hasThirdPlaceMatch = false,
            bool requireResultApproval = false,
            bool doubleRoundRobin = false,
            int? qualifiersPerGroup = null,
            int? groupsCount = null,
            KnockoutEliminationType? knockoutEliminationType = null,
            int? swissRoundsCount = null,
            int? swissKnockoutQualifiers = null,
            int? swissDirectQualifiers = null)
            => SeedTournamentAsync(
                format, participantCount, isTeam: false, teamSize: null,
                hasThirdPlaceMatch, requireResultApproval, doubleRoundRobin,
                qualifiersPerGroup, groupsCount, knockoutEliminationType,
                swissRoundsCount, swissKnockoutQualifiers, swissDirectQualifiers);

        /// <summary>
        /// Seeds a team tournament. Participants stand in for team entries (no roster rows are
        /// created — sub-match generation tolerates an empty roster, filling the per-player slots
        /// with null user ids, which is enough to assert the sub-match count and shape).
        /// </summary>
        public Task<Guid> SeedTeamTournamentAsync(
            TournamentFormat format,
            int teamCount,
            int teamSize,
            bool hasThirdPlaceMatch = false,
            bool requireResultApproval = false,
            bool doubleRoundRobin = false,
            int? qualifiersPerGroup = null,
            int? groupsCount = null)
            => SeedTournamentAsync(
                format, teamCount, isTeam: true, teamSize,
                hasThirdPlaceMatch, requireResultApproval, doubleRoundRobin,
                qualifiersPerGroup, groupsCount, knockoutEliminationType: null,
                swissRoundsCount: null, swissKnockoutQualifiers: null, swissDirectQualifiers: null);

        private async Task<Guid> SeedTournamentAsync(
            TournamentFormat format,
            int participantCount,
            bool isTeam,
            int? teamSize,
            bool hasThirdPlaceMatch,
            bool requireResultApproval,
            bool doubleRoundRobin,
            int? qualifiersPerGroup,
            int? groupsCount,
            KnockoutEliminationType? knockoutEliminationType,
            int? swissRoundsCount,
            int? swissKnockoutQualifiers,
            int? swissDirectQualifiers)
        {
            var hubId = Guid.NewGuid();
            var tournamentId = Guid.NewGuid();

            using var ctx = ReadContext();

            ctx.Add(new HubEntity { Id = hubId, UserId = OwnerUserId, Name = "Test Hub", IsDeleted = false });

            ctx.Add(new TournamentEntity
            {
                Id = tournamentId,
                HubId = hubId,
                Name = "Test Tournament",
                Status = TournamentStatus.InProgress,
                Format = format,
                IsTeamTournament = isTeam,
                TeamSize = teamSize,
                HasThirdPlaceMatch = hasThirdPlaceMatch,
                RequireResultApproval = requireResultApproval,
                DoubleRoundRobin = doubleRoundRobin,
                QualifiersPerGroup = qualifiersPerGroup,
                GroupsCount = groupsCount,
                KnockoutEliminationType = knockoutEliminationType,
                SwissRoundsCount = swissRoundsCount,
                SwissKnockoutQualifiers = swissKnockoutQualifiers,
                SwissDirectQualifiers = swissDirectQualifiers,
                IsDeleted = false,
            });

            for (int i = 0; i < participantCount; i++)
            {
                ctx.Add(new TournamentParticipantEntity
                {
                    Id = Guid.NewGuid(),
                    TournamentId = tournamentId,
                    UserId = isTeam ? null : Guid.NewGuid(),
                    TeamId = isTeam ? Guid.NewGuid() : null,
                    Seed = i + 1,
                    IsDeleted = false,
                });
            }

            await ctx.SaveChangesAsync();
            return tournamentId;
        }

        /// <summary>
        /// Creates the TournamentTeam + member rows behind a seeded participant's TeamId. The
        /// default team seeding leaves TeamId dangling (enough for generation-shape tests); flows
        /// that read the roster — captain checks, the tie-break representative submission — need
        /// the real rows. FKs are off in the SQLite harness, so no User rows are required.
        /// </summary>
        public async Task SeedTeamRosterAsync(Guid teamId, Guid tournamentId, Guid captainUserId, params Guid[] otherMemberUserIds)
        {
            using var ctx = ReadContext();

            ctx.Add(new TournamentTeamEntity
            {
                Id = teamId,
                TournamentId = tournamentId,
                TeamName = "Team " + teamId.ToString("N").Substring(0, 6),
                CaptainUserId = captainUserId,
                IsDeleted = false,
            });

            foreach (var userId in new[] { captainUserId }.Concat(otherMemberUserIds))
            {
                ctx.Add(new TournamentTeamMemberEntity
                {
                    Id = Guid.NewGuid(),
                    TeamId = teamId,
                    UserId = userId,
                    IsDeleted = false,
                });
            }

            await ctx.SaveChangesAsync();
        }

        // ---- Reads for assertions -----------------------------------------

        public List<MatchEntity> Matches(Guid tournamentId)
        {
            using var ctx = ReadContext();
            return ctx.Set<MatchEntity>()
                .AsNoTracking()
                .Where(m => m.TournamentId == tournamentId)
                .ToList();
        }

        public List<TeamMatchEntity> TeamMatches(Guid tournamentId)
        {
            using var ctx = ReadContext();
            return ctx.Set<TeamMatchEntity>()
                .AsNoTracking()
                .Where(m => m.TournamentId == tournamentId)
                .ToList();
        }

        public List<TournamentStageEntity> Stages(Guid tournamentId)
        {
            using var ctx = ReadContext();
            return ctx.Set<TournamentStageEntity>()
                .AsNoTracking()
                .Where(s => s.TournamentId == tournamentId)
                .ToList();
        }

        public List<TournamentGroupEntity> Groups(Guid tournamentId)
        {
            using var ctx = ReadContext();
            var stageIds = ctx.Set<TournamentStageEntity>()
                .AsNoTracking()
                .Where(s => s.TournamentId == tournamentId)
                .Select(s => s.Id)
                .ToList();

            return ctx.Set<TournamentGroupEntity>()
                .AsNoTracking()
                .Where(g => stageIds.Contains(g.TournamentStageId))
                .ToList();
        }

        public List<TournamentParticipantEntity> Participants(Guid tournamentId)
        {
            using var ctx = ReadContext();
            return ctx.Set<TournamentParticipantEntity>()
                .AsNoTracking()
                .Where(p => p.TournamentId == tournamentId)
                .ToList();
        }

        public TournamentEntity Tournament(Guid tournamentId)
        {
            using var ctx = ReadContext();
            return ctx.Set<TournamentEntity>().AsNoTracking().Single(t => t.Id == tournamentId);
        }

        public MatchEntity Match(Guid matchId)
        {
            using var ctx = ReadContext();
            return ctx.Set<MatchEntity>().AsNoTracking().Single(m => m.Id == matchId);
        }

        /// <summary>The seeded UserId of a participant (used to act as that participant in approval tests).</summary>
        public Guid ParticipantUserId(Guid participantId)
        {
            using var ctx = ReadContext();
            return ctx.Set<TournamentParticipantEntity>().AsNoTracking()
                .Where(p => p.Id == participantId)
                .Select(p => p.UserId!.Value)
                .Single();
        }
    }
}
