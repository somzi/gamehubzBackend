using System;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using GameHubz.Common.Consts;
using GameHubz.Common.Interfaces;
using GameHubz.Common.Models;
using GameHubz.DataModels.Consts;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Exceptions;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;
using GameHubz.Logic.SignalR;
using GameHubz.Logic.Test.Bracket;
using GameHubz.Logic.Test.Factories;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Services
{
    /// <summary>
    /// What happens to hub membership when a player enters one of the hub's tournaments:
    /// a banned player gets in through no door (HubBans), everyone else follows the hub on their
    /// first contact with it (UserHubRequestService.FollowHubOfTournament), an unfollow is kept as a
    /// soft-deleted row that stops the next automatic follow, and following never fails the entry.
    /// Runs the real services on SQLite, one fresh context per request.
    /// </summary>
    [TestFixture]
    internal sealed class HubEntryRulesTests
    {
        private BracketTestHarness harness = null!;
        private ILocalizationService loc = null!;

        private Guid owner;
        private Guid privateOwner;
        private Guid player;
        private Guid captain;
        private Guid mate;
        private Guid publicHub;
        private Guid privateHub;
        private Guid soloCup;
        private Guid privateHubCup;
        private Guid teamCup;

        [SetUp]
        public async Task SetUp()
        {
            harness = new BracketTestHarness(useSqlite: true);
            loc = new LocalizationServiceFactory().CreateService();

            owner = Guid.NewGuid();
            privateOwner = Guid.NewGuid();
            player = Guid.NewGuid();
            captain = Guid.NewGuid();
            mate = Guid.NewGuid();
            publicHub = Guid.NewGuid();
            privateHub = Guid.NewGuid();
            soloCup = Guid.NewGuid();
            privateHubCup = Guid.NewGuid();
            teamCup = Guid.NewGuid();

            using var ctx = harness.ReadContext();

            // UserRepository.GetById includes the role (an inner join), so the role row has to exist.
            var roleId = Guid.NewGuid();
            ctx.Add(new UserRoleEntity { Id = roleId, DisplayName = "User", SystemName = "BasicUser" });
            foreach (var (id, name) in new[] { (owner, "owner"), (privateOwner, "privateOwner"), (player, "player"), (captain, "captain"), (mate, "mate") })
            {
                ctx.Add(new UserEntity { Id = id, Username = name, Email = name + "@test", Region = RegionType.EUROPE, UserRoleId = roleId });
            }

            ctx.Add(new HubEntity { Id = publicHub, UserId = owner, Name = "Public Hub", IsPublic = true });
            ctx.Add(new HubEntity { Id = privateHub, UserId = privateOwner, Name = "Private Hub", IsPublic = false });
            ctx.Add(new UserHubEntity { Id = Guid.NewGuid(), UserId = owner, HubId = publicHub, HubRole = HubRole.HubOwner });
            ctx.Add(new UserHubEntity { Id = Guid.NewGuid(), UserId = privateOwner, HubId = privateHub, HubRole = HubRole.HubOwner });

            ctx.Add(OpenTournament(soloCup, publicHub, "Solo Cup"));
            ctx.Add(OpenTournament(privateHubCup, privateHub, "Private Hub Cup"));
            var team = OpenTournament(teamCup, publicHub, "Team Cup");
            team.IsTeamTournament = true;
            team.TeamSize = 2;
            ctx.Add(team);

            await ctx.SaveChangesAsync();
        }

        // ---- Bans ----------------------------------------------------------------------------

        [Test]
        public async Task SoloSignUp_ByABannedPlayer_IsRefused_AndLeavesNoTrace()
        {
            await Ban(player, publicHub);

            Assert.That(async () => await SignUp(player, soloCup),
                Throws.TypeOf<BusinessRuleException>().With.Message.EqualTo(loc["BusinessRule.YouAreBanned"]));

            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<TournamentRegistrationEntity>().Count(r => r.UserId == player), Is.Zero, "no registration");
            Assert.That(ctx.Set<UserHubEntity>().Count(m => m.UserId == player), Is.Zero, "and no membership");
        }

        [Test]
        public async Task SoloApproval_OfAPlayerBannedAfterSigningUp_IsRefused()
        {
            var registration = await SeedPendingRegistration(soloCup, player);
            await Ban(player, publicHub);

            Assert.That(async () => await As(owner).Registrations.ApproveRegistration(registration),
                Throws.TypeOf<BusinessRuleException>().With.Message.EqualTo(BannedMessage("player")));

            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<TournamentRegistrationEntity>().Single(r => r.Id == registration).Status, Is.EqualTo(TournamentRegistrationStatus.Pending));
            Assert.That(ctx.Set<TournamentParticipantEntity>().Count(p => p.UserId == player), Is.Zero);
        }

        [Test]
        public async Task BulkApproval_WithOneBannedPlayer_RefusesTheWholeBatch()
        {
            var banned = await SeedPendingRegistration(soloCup, player);
            var clean = await SeedPendingRegistration(soloCup, mate);
            await Ban(player, publicHub);

            Assert.That(async () => await As(owner).Registrations.ApproveRegistrations([banned, clean]),
                Throws.TypeOf<BusinessRuleException>().With.Message.EqualTo(BannedMessage("player")));

            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<TournamentRegistrationEntity>().Where(r => r.TournamentId == soloCup).Select(r => r.Status),
                Is.All.EqualTo(TournamentRegistrationStatus.Pending), "the organizer rejects the banned row, then approves the rest");
            Assert.That(ctx.Set<TournamentParticipantEntity>().Count(p => p.TournamentId == soloCup), Is.Zero);
        }

        [Test]
        public async Task SoloApproval_OfAPlayerWhoIsNotBanned_StillGoesThrough()
        {
            var registration = await SeedPendingRegistration(soloCup, mate);
            await Ban(player, publicHub); // someone else's ban changes nothing

            await As(owner).Registrations.ApproveRegistration(registration);

            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<TournamentParticipantEntity>().Count(p => p.TournamentId == soloCup && p.UserId == mate), Is.EqualTo(1));
        }

        public enum TeamDoor { Create, Join, AskToJoin }

        [TestCase(TeamDoor.Create)]
        [TestCase(TeamDoor.Join)]
        [TestCase(TeamDoor.AskToJoin)]
        public async Task TeamEntry_ByABannedPlayer_IsRefused(TeamDoor door)
        {
            var teamId = await SeedTeam(requiresApproval: door == TeamDoor.AskToJoin);
            await Ban(player, publicHub);

            Assert.That(async () => await EnterTeam(player, door, teamId),
                Throws.TypeOf<BusinessRuleException>().With.Message.EqualTo(loc["BusinessRule.YouAreBanned"]));

            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<TournamentTeamMemberEntity>().Count(m => m.UserId == player), Is.Zero);
            Assert.That(ctx.Set<TeamJoinRequestEntity>().Count(r => r.UserId == player), Is.Zero);
            Assert.That(ctx.Set<TournamentTeamEntity>().Count(t => t.CaptainUserId == player), Is.Zero);
        }

        [Test]
        public async Task CaptainApproval_OfAPlayerBannedAfterAsking_IsRefused()
        {
            var teamId = await SeedTeam(requiresApproval: true);
            await EnterTeam(player, TeamDoor.AskToJoin, teamId);
            await Ban(player, publicHub);

            Guid requestId;
            using (var ctx = harness.ReadContext())
            {
                requestId = ctx.Set<TeamJoinRequestEntity>().Single(r => r.UserId == player).Id!.Value;
            }

            Assert.That(async () => await As(captain).Teams.ApproveRequest(requestId),
                Throws.TypeOf<BusinessRuleException>().With.Message.EqualTo(BannedMessage("player")));

            using var check = harness.ReadContext();
            Assert.That(check.Set<TournamentTeamMemberEntity>().Count(m => m.UserId == player), Is.Zero);
        }

        [Test]
        public async Task TeamRegistration_WithABannedMember_IsRefused_NamingThatMember()
        {
            var teamId = await SeedTeam(requiresApproval: false, extraMember: mate);
            await Ban(mate, publicHub);

            Assert.That(async () => await As(captain).Registrations.RegisterTeam(teamCup, teamId),
                Throws.TypeOf<BusinessRuleException>().With.Message.EqualTo(BannedMessage("mate")));

            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<TournamentRegistrationEntity>().Count(r => r.TeamId == teamId), Is.Zero);
        }

        [Test]
        public async Task TeamApproval_OfATeamWithAMemberBannedAfterRegistering_IsRefused()
        {
            var teamId = await SeedTeam(requiresApproval: false, extraMember: mate);
            await As(captain).Registrations.RegisterTeam(teamCup, teamId);
            await Ban(mate, publicHub);

            Guid registration;
            using (var ctx = harness.ReadContext())
            {
                registration = ctx.Set<TournamentRegistrationEntity>().Single(r => r.TeamId == teamId).Id!.Value;
            }

            Assert.That(async () => await As(owner).Registrations.ApproveRegistration(registration),
                Throws.TypeOf<BusinessRuleException>().With.Message.EqualTo(BannedMessage("mate")));
        }

        [Test]
        public async Task ParticipantSwap_ToABannedPlayerAddedBackAsMember_IsRefused()
        {
            // AddMember doesn't lift a ban, so membership alone can't be what lets a player in.
            using (var ctx = harness.ReadContext())
            {
                ctx.Add(new TournamentParticipantEntity { Id = Guid.NewGuid(), TournamentId = soloCup, UserId = mate, Seed = 1 });
                await ctx.SaveChangesAsync();
            }

            await As(owner).Members.BanMember(publicHub, player);
            await As(owner).Members.AddMember(publicHub, player, HubRole.HubMember);

            Assert.That(async () => await As(owner).Participants.SwapParticipant(soloCup, new ParticipantSwapRequest { OutgoingUserId = mate, IncomingUserId = player }),
                Throws.TypeOf<BusinessRuleException>().With.Message.EqualTo(BannedMessage("player")));
        }

        [Test]
        public async Task SwapCandidates_LeaveOutABannedPlayerAddedBackAsMember()
        {
            using (var ctx = harness.ReadContext())
            {
                ctx.Add(new TournamentParticipantEntity { Id = Guid.NewGuid(), TournamentId = soloCup, UserId = mate, Seed = 1 });
                ctx.Add(new UserHubEntity { Id = Guid.NewGuid(), UserId = captain, HubId = publicHub, HubRole = HubRole.HubMember });
                await ctx.SaveChangesAsync();
            }

            await As(owner).Members.BanMember(publicHub, player);
            await As(owner).Members.AddMember(publicHub, player, HubRole.HubMember);

            var candidates = await As(owner).Participants.GetSwapCandidates(soloCup, null, 0);

            Assert.That(candidates.Select(c => c.UserId), Does.Not.Contain(player), "the swap would refuse them");
            Assert.That(candidates.Select(c => c.UserId), Does.Contain(captain), "other members are still offered");
        }

        // ---- Following on entry --------------------------------------------------------------

        [Test]
        public async Task SoloSignUp_InAPublicHub_FollowsTheHub()
        {
            await SignUp(player, soloCup);

            using var ctx = harness.ReadContext();
            var membership = ctx.Set<UserHubEntity>().Single(m => m.UserId == player && m.HubId == publicHub);
            Assert.That(membership.HubRole, Is.EqualTo(HubRole.HubMember));
        }

        [Test]
        public async Task SoloSignUp_InAPrivateHub_FilesAJoinRequest_NotAMembership()
        {
            await SignUp(player, privateHubCup);

            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<UserHubEntity>().Count(m => m.UserId == player), Is.Zero, "a private hub's owner decides who's in");
            Assert.That(ctx.Set<UserHubRequestEntity>().Single(r => r.UserId == player && r.HubId == privateHub).Status,
                Is.EqualTo(JoinRequestStatus.Pending));
        }

        [Test]
        public async Task SoloSignUp_ByTheOwner_AddsNoSecondMembership()
        {
            await SignUp(owner, soloCup);

            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<UserHubEntity>().IgnoreQueryFilters().Count(m => m.UserId == owner && m.HubId == publicHub), Is.EqualTo(1));
        }

        [TestCase(TeamDoor.Create)]
        [TestCase(TeamDoor.Join)]
        [TestCase(TeamDoor.AskToJoin)]
        public async Task TeamEntry_FollowsTheHub(TeamDoor door)
        {
            var teamId = await SeedTeam(requiresApproval: door == TeamDoor.AskToJoin);

            await EnterTeam(player, door, teamId);

            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<UserHubEntity>().Count(m => m.UserId == player && m.HubId == publicHub), Is.EqualTo(1));
        }

        [Test]
        public async Task TeamRegistration_FollowsTheHubForTheCaptain()
        {
            // A team made before automatic following: the captain is followed when registering it.
            var teamId = await SeedTeam(requiresApproval: false);

            await As(captain).Registrations.RegisterTeam(teamCup, teamId);

            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<UserHubEntity>().Count(m => m.UserId == captain && m.HubId == publicHub), Is.EqualTo(1));
        }

        [Test]
        public async Task SignUp_AfterUnfollowing_DoesNotFollowAgain()
        {
            await As(player).Follow.RequestJoin(publicHub);
            await As(player).Members.Unfollow(player, publicHub);

            await SignUp(player, soloCup);

            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<UserHubEntity>().Count(m => m.UserId == player && m.HubId == publicHub), Is.Zero, "the player chose to leave");
            Assert.That(ctx.Set<TournamentRegistrationEntity>().Count(r => r.UserId == player), Is.EqualTo(1), "and still signed up");
        }

        [Test]
        public async Task SignUp_AfterARejectedJoinRequest_FilesNoNewRequest()
        {
            using (var ctx = harness.ReadContext())
            {
                ctx.Add(new UserHubRequestEntity { Id = Guid.NewGuid(), HubId = privateHub, UserId = player, Status = JoinRequestStatus.Rejected });
                await ctx.SaveChangesAsync();
            }

            await SignUp(player, privateHubCup);

            using var check = harness.ReadContext();
            Assert.That(check.Set<UserHubRequestEntity>().Count(r => r.UserId == player && r.HubId == privateHub), Is.EqualTo(1), "the owner already said no");
        }

        [Test]
        public async Task SignUp_InATournamentOfADeletedHub_FollowsNothing()
        {
            using (var ctx = harness.ReadContext())
            {
                ctx.Set<HubEntity>().IgnoreQueryFilters().Single(h => h.Id == publicHub).IsDeleted = true;
                await ctx.SaveChangesAsync();
            }

            await SignUp(player, soloCup);

            using var check = harness.ReadContext();
            Assert.That(check.Set<UserHubEntity>().IgnoreQueryFilters().Count(m => m.UserId == player), Is.Zero);
            Assert.That(check.Set<TournamentRegistrationEntity>().Count(r => r.UserId == player), Is.EqualTo(1));
        }

        [Test]
        public async Task FollowingThatFails_NeverFailsTheSignUp()
        {
            // The follow saves the membership, then its cache cleanup blows up.
            await SignUp(player, soloCup, followCache: new ThrowingCache());

            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<TournamentRegistrationEntity>().Count(r => r.UserId == player), Is.EqualTo(1));
        }

        [Test]
        public async Task Following_ClearsTheJoinedAndDiscoveryListsAndTheRole()
        {
            var joined = $"user_joined_hubs:{player}:p:0";
            var discovery = $"user_discovery_hubs:{player}:p:0";
            var role = $"user_hub_role:{player}:{publicHub}";
            await harness.Cache.SetAsync(joined, "stale");
            await harness.Cache.SetAsync(discovery, "stale");
            await harness.Cache.SetAsync<int?>(role, -1);

            await SignUp(player, soloCup);

            Assert.That(await harness.Cache.GetAsync<string>(joined), Is.Null);
            Assert.That(await harness.Cache.GetAsync<string>(discovery), Is.Null);
            Assert.That(await harness.Cache.GetAsync<int?>(role), Is.Null);
        }

        // ---- Unfollow ------------------------------------------------------------------------

        [Test]
        public async Task Unfollow_KeepsTheRowAsSoftDeleted_AndAManualFollowAddsANewOne()
        {
            await As(player).Follow.RequestJoin(publicHub);
            await As(player).Members.Unfollow(player, publicHub);

            using (var ctx = harness.ReadContext())
            {
                Assert.That(ctx.Set<UserHubEntity>().Count(m => m.UserId == player), Is.Zero, "no longer following");
                Assert.That(ctx.Set<UserHubEntity>().IgnoreQueryFilters().Single(m => m.UserId == player).IsDeleted, Is.True, "but the row says they left");
            }

            await As(player).Follow.RequestJoin(publicHub);

            using var check = harness.ReadContext();
            Assert.That(check.Set<UserHubEntity>().Count(m => m.UserId == player), Is.EqualTo(1), "following again by hand works");
            Assert.That(check.Set<UserHubEntity>().IgnoreQueryFilters().Count(m => m.UserId == player), Is.EqualTo(2));
        }

        // ---- Helpers -------------------------------------------------------------------------

        private sealed record Door(
            TournamentRegistrationService Registrations,
            TournamentTeamService Teams,
            TournamentParticipantService Participants,
            UserHubService Members,
            UserHubRequestService Follow);

        /// <summary>The real services as <paramref name="userId"/>, over a fresh context â€” one request.</summary>
        private Door As(Guid userId, ICacheService? followCache = null)
        {
            var factory = new TestUnitOfWorkFactory(harness.ReadContext(), loc);
            var reader = Reader(userId);
            var mapper = new MapperFactory().CreateService();
            var search = new SearchServiceFactory().CreateService();
            var functions = new ServiceFunctionsFactory().CreateService();
            var cache = harness.Cache;

            var members = new UserHubService(factory, mapper, loc, new InlineValidator<UserHubEntity>(), search, functions, reader, cache);
            var auth = new TournamentAuthorizationService(factory, reader, loc, cache, members);
            var badges = new BadgeService(factory, reader, loc, new Mock<IHubContext<UserHub>>().Object,
                new BadgeRefreshQueue(new Mock<IServiceScopeFactory>().Object, NullLogger<BadgeRefreshQueue>.Instance));
            var follow = new UserHubRequestService(factory, mapper, loc, new InlineValidator<UserHubRequestEntity>(), search, functions,
                reader, followCache ?? cache, members, harness.Notifications, badges, NullLogger<UserHubRequestService>.Instance);
            var participants = new TournamentParticipantService(factory, mapper, loc, new InlineValidator<TournamentParticipantEntity>(),
                search, functions, reader, cache, auth, badges, harness.Notifications, new ConfigurationBuilder().Build());
            var registrations = new TournamentRegistrationService(factory, mapper, loc, new InlineValidator<TournamentRegistrationEntity>(),
                search, functions, reader, participants, cache, badges, auth, follow);
            var teams = new TournamentTeamService(factory, reader, loc, cache, harness.Notifications, badges, auth, follow);

            return new Door(registrations, teams, participants, members, follow);
        }

        /// <summary>The refusal that names someone else — an approval, a team roster, a swap.</summary>
        private string BannedMessage(string username) => string.Format(loc["BusinessRule.UserBannedFromHub"], username);

        private static IUserContextReader Reader(Guid userId)
        {
            var token = new TokenUserInfo { UserId = userId, Username = "user", Role = "BasicUser", RoleEnum = UserRoleEnum.BasicUser };
            var reader = new Mock<IUserContextReader>();
            reader.Setup(x => x.GetTokenUserInfoFromContext()).Returns(Task.FromResult<TokenUserInfo?>(token));
            reader.Setup(x => x.GetTokenUserInfoFromContextThrowIfNull()).Returns(Task.FromResult(token));
            reader.Setup(x => x.GetRequestData()).Returns(Task.FromResult(new UserRequestData(token, Languages.English)));
            return reader.Object;
        }

        private Task SignUp(Guid userId, Guid tournamentId, ICacheService? followCache = null) =>
            As(userId, followCache).Registrations.SaveEntity(new TournamentRegistrationPost
            {
                TournamentId = tournamentId,
                UserId = userId,
                Status = TournamentRegistrationStatus.Pending,
            });

        private Task EnterTeam(Guid userId, TeamDoor door, Guid teamId) => door switch
        {
            TeamDoor.Create => As(userId).Teams.CreateTeam(new CreateTeamRequest { TournamentId = teamCup, TeamName = "New Team" }),
            TeamDoor.Join => As(userId).Teams.JoinTeam(teamId),
            _ => As(userId).Teams.RequestJoin(teamId),
        };

        private async Task Ban(Guid userId, Guid hubId)
        {
            using var ctx = harness.ReadContext();
            ctx.Add(new UserHubBanEntity { Id = Guid.NewGuid(), UserId = userId, HubId = hubId, BannedById = owner });
            await ctx.SaveChangesAsync();
        }

        private async Task<Guid> SeedPendingRegistration(Guid tournamentId, Guid userId)
        {
            var id = Guid.NewGuid();
            using var ctx = harness.ReadContext();
            ctx.Add(new TournamentRegistrationEntity { Id = id, TournamentId = tournamentId, UserId = userId, Status = TournamentRegistrationStatus.Pending });
            await ctx.SaveChangesAsync();
            return id;
        }

        /// <summary>A team in the team cup, captained by <see cref="captain"/>, with one free place.</summary>
        private async Task<Guid> SeedTeam(bool requiresApproval, Guid? extraMember = null)
        {
            var id = Guid.NewGuid();
            using var ctx = harness.ReadContext();
            ctx.Add(new TournamentTeamEntity { Id = id, TournamentId = teamCup, TeamName = "Captain's Team", CaptainUserId = captain, RequiresApproval = requiresApproval });
            ctx.Add(new TournamentTeamMemberEntity { Id = Guid.NewGuid(), TeamId = id, UserId = captain });
            if (extraMember.HasValue)
            {
                ctx.Add(new TournamentTeamMemberEntity { Id = Guid.NewGuid(), TeamId = id, UserId = extraMember });
            }

            await ctx.SaveChangesAsync();
            return id;
        }

        private static TournamentEntity OpenTournament(Guid id, Guid hubId, string name) => new()
        {
            Id = id,
            HubId = hubId,
            Name = name,
            Status = TournamentStatus.RegistrationOpen,
            Format = TournamentFormat.SingleElimination,
            Region = RegionType.GLOBAL,
            MaxPlayers = 16,
            StartDate = DateTime.UtcNow.AddDays(7),
        };

        /// <summary>A cache whose every write fails â€” stands in for whatever can go wrong mid-follow.</summary>
        private sealed class ThrowingCache : ICacheService
        {
            public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
            public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) => throw new InvalidOperationException("cache down");
            public Task RemoveAsync(string key) => throw new InvalidOperationException("cache down");
            public Task RemoveByPatternAsync(string pattern) => throw new InvalidOperationException("cache down");
            public Task<long> IncrementAsync(string key, TimeSpan window) => throw new InvalidOperationException("cache down");
            public Task DecrementCounterAsync(string key) => throw new InvalidOperationException("cache down");
            public Task<long> GetCounterAsync(string key) => throw new InvalidOperationException("cache down");
            public Task RemoveCounterAsync(string key) => throw new InvalidOperationException("cache down");
        }
    }
}
