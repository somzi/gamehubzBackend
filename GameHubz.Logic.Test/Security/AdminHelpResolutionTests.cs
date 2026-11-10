using System;
using System.Linq;
using System.Threading.Tasks;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Exceptions;
using GameHubz.Logic.Test.Bracket;
using GameHubz.Logic.Test.Factories;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Security
{
    [TestFixture]
    internal sealed class AdminHelpResolutionTests
    {
        [TestCase(MatchStatus.Pending)]
        [TestCase(MatchStatus.Scheduled)]
        [TestCase(MatchStatus.Completed)]
        public async Task RequesterCanResolveWithoutChangingTheResult(MatchStatus status)
        {
            var (h, match, requester, _) = await Seed(status);
            await h.NewMatchServiceAsUser(requester).ResolveAdminHelp(match.Id!.Value);
            var saved = h.Match(match.Id.Value);
            Assert.Multiple(() =>
            {
                Assert.That(saved.AdminHelpRequested, Is.False);
                Assert.That(saved.AdminHelpRequestedByUserId, Is.Null);
                Assert.That(saved.Status, Is.EqualTo(status));
                Assert.That(saved.HomeUserScore, Is.EqualTo(2));
                Assert.That(saved.AwayUserScore, Is.EqualTo(1));
                Assert.That(saved.ProposedByUserId, Is.EqualTo(requester));
            });
            using var context = h.ReadContext();
            var note = context.Set<MatchChatEntity>().Single(c => c.MatchId == match.Id);
            Assert.That(note.UserId, Is.EqualTo(requester));
            Assert.That(note.Content, Is.Not.Empty);
            Assert.That(note.CreatedOn, Is.GreaterThan(DateTime.UtcNow.AddMinutes(-1)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OpponentAndOutsiderCannotResolveSomeoneElsesRequest(bool outsider)
        {
            var (h, match, _, opponent) = await Seed(MatchStatus.Completed);
            var caller = outsider ? Guid.NewGuid() : opponent;
            await h.DenyManageFor(caller, match.TournamentId);
            Assert.ThrowsAsync<BusinessRuleException>(() =>
                h.NewMatchServiceAsUser(caller).ResolveAdminHelp(match.Id!.Value));
            Assert.That(h.Match(match.Id!.Value).AdminHelpRequested, Is.True);
            using var context = h.ReadContext();
            Assert.That(context.Set<MatchChatEntity>().Any(), Is.False);
        }

        [Test]
        public async Task ManagerCanResolveAndRepeatedResolveDoesNotDuplicateHistory()
        {
            var (h, match, _, _) = await Seed(MatchStatus.Completed);
            var manager = Guid.NewGuid();
            await h.AllowManageFor(manager, match.TournamentId);
            await h.NewMatchServiceAsUser(manager).ResolveAdminHelp(match.Id!.Value);
            await h.NewMatchServiceAsUser(manager).ResolveAdminHelp(match.Id.Value);
            Assert.That(h.Match(match.Id.Value).AdminHelpRequested, Is.False);
            using var context = h.ReadContext();
            Assert.That(context.Set<MatchChatEntity>().Count(), Is.EqualTo(1));
            Assert.That(context.Set<MatchChatEntity>().Single().UserId, Is.EqualTo(manager));
        }

        [Test]
        public async Task HelpWriteCannotOverwriteAResultSavedAfterItsAuthorizationRead()
        {
            var (h, snapshot, requester, _) = await Seed(MatchStatus.Scheduled, useSqlite: true);
            using (var concurrent = h.ReadContext())
            {
                var match = concurrent.Set<MatchEntity>().Single(m => m.Id == snapshot.Id);
                match.Status = MatchStatus.Completed;
                match.HomeUserScore = 4;
                match.ProposedByUserId = null;
                await concurrent.SaveChangesAsync();
            }
            using (var context = h.ReadContext())
            {
                var factory = new TestUnitOfWorkFactory(context, new LocalizationServiceFactory().CreateService());
                snapshot.AdminHelpRequested = false;
                snapshot.AdminHelpRequestedByUserId = null;
                snapshot.AdminHelpRequestedOn = null;
                factory.CreateAppUnitOfWork().MatchRepository.UpdateAdminHelp(snapshot, requester);
                await context.SaveChangesAsync();
            }
            var saved = h.Match(snapshot.Id!.Value);
            Assert.That(saved.AdminHelpRequested, Is.False);
            Assert.That(saved.Status, Is.EqualTo(MatchStatus.Completed));
            Assert.That(saved.HomeUserScore, Is.EqualTo(4));
            Assert.That(saved.ProposedByUserId, Is.Null);
        }

        private static async Task<(BracketTestHarness, MatchEntity, Guid, Guid)> Seed(MatchStatus status, bool useSqlite = false)
        {
            var h = new BracketTestHarness(useSqlite);
            var tid = await h.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 2);
            var requester = Guid.NewGuid();
            var opponent = Guid.NewGuid();
            var match = new MatchEntity
            {
                Id = Guid.NewGuid(), TournamentId = tid, Status = status,
                HomeUserId = requester, AwayUserId = opponent,
                HomeUserScore = 2, AwayUserScore = 1, ProposedByUserId = requester,
                AdminHelpRequested = true, AdminHelpRequestedByUserId = requester,
                AdminHelpRequestedOn = DateTime.UtcNow.AddHours(-1),
            };
            using var context = h.ReadContext();
            context.Set<MatchEntity>().Add(match);
            await context.SaveChangesAsync();
            await h.DenyManageFor(requester, tid);
            return (h, match, requester, opponent);
        }
    }
}
