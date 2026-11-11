using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Test.Bracket;
using GameHubz.Logic.Test.Factories;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Services
{
    /// <summary>
    /// The player's own tabs (Upcoming / Live / Completed) list every tournament they play or played
    /// in — whatever the hub follow, region, exclusive role, privacy or the hub having been deleted —
    /// while AvailableToJoin keeps its discovery rules. Plus: a tournament whose hub was soft-deleted
    /// still opens (overview, structure, match details) and can still be played. Runs on SQLite.
    /// </summary>
    [TestFixture]
    internal sealed class ParticipantTournamentTabsTests
    {
        private BracketTestHarness harness = null!;
        private Guid deletedHub;
        private Guid liveHub;
        private Guid player;
        private Guid outsider;
        private Dictionary<string, Guid> ids = null!;

        [SetUp]
        public async Task SetUp()
        {
            harness = new BracketTestHarness(useSqlite: true);
            deletedHub = Guid.NewGuid();
            liveHub = Guid.NewGuid();
            player = Guid.NewGuid();
            outsider = Guid.NewGuid();
            ids = new Dictionary<string, Guid>();

            using var ctx = harness.ReadContext();

            ctx.Add(new HubEntity { Id = deletedHub, UserId = BracketTestHarness.OwnerUserId, Name = "Gone Hub", AvatarUrl = "https://cdn/gone.png", IsDeleted = true });
            ctx.Add(new HubEntity { Id = liveHub, UserId = BracketTestHarness.OwnerUserId, Name = "Live Hub", IsDeleted = false });

            // The player follows nothing. The outsider follows both hubs — the deleted hub's
            // membership row survives the soft delete, which is exactly what must not resurrect it.
            ctx.Add(User(player));
            ctx.Add(User(outsider));
            ctx.Add(new UserHubEntity { Id = Guid.NewGuid(), UserId = outsider, HubId = deletedHub, HubRole = HubRole.HubMember });
            ctx.Add(new UserHubEntity { Id = Guid.NewGuid(), UserId = outsider, HubId = liveHub, HubRole = HubRole.HubMember });

            Tournament(ctx, "liveGonePublic", deletedHub, TournamentStatus.InProgress);
            Tournament(ctx, "liveGonePrivate", deletedHub, TournamentStatus.InProgress, isPrivate: true);
            Tournament(ctx, "doneGonePublic", deletedHub, TournamentStatus.Completed);
            Tournament(ctx, "doneGonePrivate", deletedHub, TournamentStatus.Completed, isPrivate: true);
            Tournament(ctx, "upcomingGonePublic", deletedHub, TournamentStatus.RegistrationClosed);
            Tournament(ctx, "cancelledGone", deletedHub, TournamentStatus.Cancelled);
            Tournament(ctx, "statusDeletedGone", deletedHub, TournamentStatus.Deleted);
            Tournament(ctx, "softDeletedTournament", deletedHub, TournamentStatus.Completed, isDeleted: true);
            // Hub not followed, another region, exclusive without the role — still the player's own.
            Tournament(ctx, "liveUnfollowedExclusiveNa", liveHub, TournamentStatus.InProgress, region: RegionType.NA, isExclusive: true);
            // AvailableToJoin probes: nobody in them.
            Tournament(ctx, "openGone", deletedHub, TournamentStatus.RegistrationOpen, playerIn: false);
            Tournament(ctx, "openLive", liveHub, TournamentStatus.RegistrationOpen, playerIn: false);

            await ctx.SaveChangesAsync();
        }

        [Test]
        public async Task LiveTab_ListsEveryTournamentThePlayerIsIn_EvenWithTheHubDeleted()
        {
            var page = await Tab(player, TournamentUserStatus.Live);

            Assert.That(Keys(page), Is.EquivalentTo(new[] { "liveGonePublic", "liveGonePrivate", "liveUnfollowedExclusiveNa" }));
            Assert.That(page.Count, Is.EqualTo(3), "count agrees with the page");

            var gone = page.Tournaments.Single(t => t.Id == ids["liveGonePublic"]);
            Assert.That(gone.HubName, Is.Null, "no hub to name");
            Assert.That(gone.HubAvatarUrl, Is.Null, "no hub avatar either");
            Assert.That(page.Tournaments.Single(t => t.Id == ids["liveUnfollowedExclusiveNa"]).HubName, Is.EqualTo("Live Hub"));
        }

        [Test]
        public async Task CompletedTab_KeepsPlayedTournaments_ButNotSoftDeletedOnes()
        {
            var page = await Tab(player, TournamentUserStatus.Completed);

            Assert.That(Keys(page), Is.EquivalentTo(new[] { "doneGonePublic", "doneGonePrivate" }));
            Assert.That(page.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task UpcomingTab_ListsTheClosedRegistration_NeverCancelledOrDeleted()
        {
            var upcoming = await Tab(player, TournamentUserStatus.Upcoming);
            Assert.That(Keys(upcoming), Is.EquivalentTo(new[] { "upcomingGonePublic" }));

            var everyOwnTab = (await Tab(player, TournamentUserStatus.Live)).Tournaments
                .Concat((await Tab(player, TournamentUserStatus.Completed)).Tournaments)
                .Concat(upcoming.Tournaments)
                .Select(t => t.Id)
                .ToList();
            Assert.That(everyOwnTab, Does.Not.Contain(ids["cancelledGone"]));
            Assert.That(everyOwnTab, Does.Not.Contain(ids["statusDeletedGone"]));
            Assert.That(everyOwnTab, Does.Not.Contain(ids["softDeletedTournament"]));
        }

        [Test]
        public async Task NonParticipant_GetsNoneOfThemInTheirOwnTabs()
        {
            foreach (var tab in new[] { TournamentUserStatus.Upcoming, TournamentUserStatus.Live, TournamentUserStatus.Completed })
            {
                var page = await Tab(outsider, tab);
                Assert.That(page.Tournaments, Is.Empty, $"{tab}: following the hubs is not taking part");
                Assert.That(page.Count, Is.Zero, $"{tab}: count");
            }
        }

        [Test]
        public async Task AvailableToJoin_KeepsItsDiscoveryRules()
        {
            // Only followed, still-existing hubs — the deleted hub's leftover membership row doesn't count.
            Assert.That(Keys(await Tab(outsider, TournamentUserStatus.AvailableToJoin)), Is.EquivalentTo(new[] { "openLive" }));
            // The player follows nothing, so nothing to discover — playing elsewhere changes no rule here.
            Assert.That((await Tab(player, TournamentUserStatus.AvailableToJoin)).Tournaments, Is.Empty);
        }

        [Test]
        public async Task TournamentOfADeletedHub_StillOpensAndPlays_ForItsParticipant()
        {
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4, requireResultApproval: true);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var match = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);
            var participant = harness.ParticipantUserId(match.HomeParticipantId!.Value);
            Guid hubId;
            using (var ctx = harness.ReadContext())
            {
                var tournament = ctx.Set<TournamentEntity>().Single(t => t.Id == tournamentId);
                tournament.MaxPlayers = 4;
                tournament.CreatedBy = BracketTestHarness.OwnerUserId;
                hubId = tournament.HubId!.Value;
                ctx.Set<HubEntity>().IgnoreQueryFilters().Single(h => h.Id == hubId).IsDeleted = true;
                ctx.Add(User(participant));
                await ctx.SaveChangesAsync();
            }

            Assert.That((await Tab(participant, TournamentUserStatus.Live)).Tournaments.Select(t => t.Id), Does.Contain(tournamentId));

            var overview = await harness.NewTournamentService().GetOverviewV3(tournamentId);
            Assert.That(overview.HubName, Is.Null, "overview renders without the hub");
            Assert.That(overview.HubId, Is.EqualTo(hubId));

            Assert.That(async () => await harness.NewService().GetTournamentStructureV3(tournamentId), Throws.Nothing);

            var uow = new TestUnitOfWorkFactory(harness.ReadContext(), new LocalizationServiceFactory().CreateService()).CreateAppUnitOfWork();
            Assert.That(await uow.MatchRepository.GetWithEvidence(match.Id!.Value), Is.Not.Null, "match details");
            var approval = await uow.TournamentRepository.GetApprovalContext(tournamentId);
            Assert.That(approval!.HubOwnerUserId, Is.EqualTo(Guid.Empty), "no hub, no hub owner");

            // Reporting reads the approval context — it used to throw on the missing hub.
            await harness.DenyManageFor(participant, tournamentId);
            await harness.NewServiceAsUser(participant).UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
            });
            Assert.That(harness.Match(match.Id!.Value).ProposedByUserId, Is.EqualTo(participant));
        }

        private Task<TournamentPagedResponse> Tab(Guid userId, TournamentUserStatus status) =>
            harness.NewTournamentService().GetTournamentPagedForUser(
                userId, new UserTournamentRequest { Status = status, Page = 0, PageSize = 50 }, includeScheduled: true);

        private List<string> Keys(TournamentPagedResponse page) =>
            page.Tournaments.Select(t => ids.Single(kv => kv.Value == t.Id).Key).ToList();

        private static UserEntity User(Guid id) => new()
        {
            Id = id,
            Username = "u" + id.ToString("N").Substring(0, 8),
            Email = id.ToString("N") + "@test",
            Region = RegionType.EUROPE,
            UserRoleId = Guid.NewGuid(),
            IsDeleted = false,
        };

        private void Tournament(
            DbContext ctx,
            string key,
            Guid hubId,
            TournamentStatus status,
            bool isPrivate = false,
            bool playerIn = true,
            bool isDeleted = false,
            RegionType region = RegionType.GLOBAL,
            bool isExclusive = false)
        {
            var id = Guid.NewGuid();
            ids[key] = id;

            ctx.Add(new TournamentEntity
            {
                Id = id,
                HubId = hubId,
                Name = key,
                Status = status,
                Format = TournamentFormat.SingleElimination,
                Region = region,
                IsPrivate = isPrivate,
                IsExclusive = isExclusive,
                MaxPlayers = 8,
                StartDate = DateTime.UtcNow.AddDays(1),
                IsDeleted = isDeleted,
            });

            if (playerIn)
            {
                ctx.Add(new TournamentParticipantEntity { Id = Guid.NewGuid(), TournamentId = id, UserId = player, Seed = 1, IsDeleted = false });
            }
        }
    }
}
