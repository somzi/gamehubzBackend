using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;
using NUnit.Framework;

using GameHubz.Common.Interfaces;
using GameHubz.Data.Context;
using GameHubz.DataModels.Consts;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Localization;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;
using GameHubz.Logic.SignalR;
using GameHubz.Logic.Test.Bracket;

namespace GameHubz.Logic.Test.Services
{
    /// <summary>
    /// The inbox row is written as a SIDE EFFECT of sending a push, inside a try/catch that swallows
    /// failures on purpose — a database hiccup must never stop someone being told their match is starting.
    /// That makes this the one path in the feature whose failure mode is completely silent: if recording
    /// breaks, pushes keep arriving, the app keeps working, and the inbox is simply empty forever.
    ///
    /// <para>
    /// <see cref="NotificationServiceLocalizationTests"/> cannot see any of it: every recipient there is
    /// built with the two-argument <c>PushRecipient</c>, whose <c>UserId</c> is null, so the recording
    /// branch is never entered and the payloads it captures carry no <c>notificationId</c>. These tests
    /// drive the same service through <see cref="PushRecipient.ForUser"/> against a real
    /// <see cref="NotificationInboxService"/> over SQLite, so the rows, the per-recipient id injection and
    /// the Action/Update split are actually asserted.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class NotificationInboxRecordingTests
    {
        private const string TitleKey = "Push.ResultToConfirm.Title";
        private const string TitleEn = "Result to confirm";
        private const string TitleEs = "Resultado por confirmar";
        private const string BodyKey = "Push.TournamentWon.Body";

        private SqliteConnection connection = null!;
        private TestApplicationContext context = null!;
        private IAppUnitOfWork unitOfWork = null!;
        private NotificationService service = null!;
        private CapturingHandler handler = null!;

        /// <summary>Set by <see cref="FailingScopeFactory"/> tests to prove a broken inbox still sends.</summary>
        private bool inboxThrows;

        [SetUp]
        public void SetUp()
        {
            // Foreign keys off, as in BracketTestHarness: a notification's UserId points at a User row we
            // have no reason to seed here.
            this.connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
            this.connection.Open();

            var options = new DbContextOptionsBuilder<TestApplicationContext>()
                .UseSqlite(this.connection)
                .Options;

            this.context = new TestApplicationContext(options);
            this.context.Database.EnsureCreated();

            var localization = new LocalizationService(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Language"] = Languages.English })
                    .Build(),
                httpContextAccessor: null);

            var factory = new TestUnitOfWorkFactory(this.context, localization);
            this.unitOfWork = factory.CreateAppUnitOfWork();

            var inbox = new NotificationInboxService(
                factory,
                new Mock<IUserContextReader>().Object,
                localization,
                BuildHubContext());

            this.handler = new CapturingHandler();
            var httpFactory = new Mock<IHttpClientFactory>();
            httpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
                       .Returns(() => new HttpClient(this.handler, disposeHandler: false));

            this.inboxThrows = false;

            this.service = new NotificationService(
                httpFactory.Object,
                NullLogger<NotificationService>.Instance,
                new TestScopeFactory(inbox, () => this.inboxThrows),
                localization);
        }

        [TearDown]
        public void TearDown()
        {
            this.context.Dispose();
            this.connection.Dispose();
        }

        // ── Harness ──────────────────────────────────────────────────────────────────────────────

        /// <summary>Hands out one long-lived inbox service, or throws to simulate a broken scope.</summary>
        private sealed class TestScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
        {
            private readonly NotificationInboxService inbox;
            private readonly Func<bool> shouldThrow;

            public TestScopeFactory(NotificationInboxService inbox, Func<bool> shouldThrow)
            {
                this.inbox = inbox;
                this.shouldThrow = shouldThrow;
            }

            public IServiceScope CreateScope()
                => this.shouldThrow() ? throw new InvalidOperationException("no scope") : this;

            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType)
                => serviceType == typeof(NotificationInboxService) ? this.inbox : null;

            public void Dispose() { }
        }

        /// <summary>A hub context whose SendAsync completes, so a counter push is a real call and not a swallowed null.</summary>
        private static IHubContext<UserHub> BuildHubContext()
        {
            var proxy = new Mock<IClientProxy>();
            proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

            var clients = new Mock<IHubClients>();
            clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);

            var hub = new Mock<IHubContext<UserHub>>();
            hub.Setup(h => h.Clients).Returns(clients.Object);

            return hub.Object;
        }

        private sealed class CapturingHandler : HttpMessageHandler
        {
            public List<List<CapturedMessage>> Batches { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string json = await request.Content!.ReadAsStringAsync(cancellationToken);
                this.Batches.Add(JsonSerializer.Deserialize<List<CapturedMessage>>(
                    json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":[]}"),
                };
            }
        }

        private sealed class CapturedMessage
        {
            public string To { get; set; } = "";
            public string Title { get; set; } = "";
            public string Body { get; set; } = "";

            /// <summary>The whole payload, so the injected notificationId can be read back.</summary>
            public JsonElement? Data { get; set; }
        }

        private List<CapturedMessage> Sent()
            => this.handler.Batches.SelectMany(b => b).ToList();

        private static string? NotificationIdOf(CapturedMessage message)
            => message.Data is JsonElement data
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("notificationId", out JsonElement id)
                    ? id.GetString()
                    : null;

        /// <summary>Rows straight out of the store, on a context that has not cached this test's writes.</summary>
        private async Task<List<NotificationEntity>> RowsFor(Guid userId)
            => await this.context.Set<NotificationEntity>()
                .AsNoTracking()
                .Where(n => n.UserId == userId)
                .ToListAsync();

        private Task SendAsync(IEnumerable<PushRecipient> recipients, object? data = null)
            => this.service.SendLocalizedToManyAsync(
                recipients, PushText.FromKey(TitleKey), PushText.FromKey(BodyKey), data);

        // ── Recording ────────────────────────────────────────────────────────────────────────────

        [Test]
        public async Task ARecipientWithAUserId_GetsAnInboxRow()
        {
            Guid userId = Guid.NewGuid();

            await this.SendAsync(new[] { PushRecipient.ForUser(userId, "tok", Languages.English) });

            var rows = await this.RowsFor(userId);

            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Title, Is.EqualTo(TitleEn));
            Assert.That(rows[0].Body, Is.Not.Empty);
            Assert.That(rows[0].ReadOn, Is.Null, "a freshly recorded notification is unread");
            Assert.That(rows[0].SeenOn, Is.Null, "and unseen, so it lights the bell");
        }

        [Test]
        public async Task AUserWithoutAPushToken_StillGetsAnInboxRow()
        {
            Guid userId = Guid.NewGuid();

            // The whole point of the inbox: this account would otherwise never learn about it at all.
            await this.SendAsync(new[] { PushRecipient.ForUser(userId, null, Languages.English) });

            Assert.That(await this.RowsFor(userId), Has.Count.EqualTo(1));
            Assert.That(this.handler.Batches, Is.Empty, "no token means no Expo request");
        }

        [Test]
        public async Task TheRowIsWordedInTheRecipientsOwnLanguage_NotTheSenders()
        {
            Guid spanish = Guid.NewGuid();
            Guid english = Guid.NewGuid();

            await this.SendAsync(new[]
            {
                PushRecipient.ForUser(spanish, "tok-es", Languages.Spanish),
                PushRecipient.ForUser(english, "tok-en", Languages.English),
            });

            Assert.That((await this.RowsFor(spanish)).Single().Title, Is.EqualTo(TitleEs));
            Assert.That((await this.RowsFor(english)).Single().Title, Is.EqualTo(TitleEn));
        }

        [Test]
        public async Task EveryRecipientsPushCarriesTheirOwnRowId()
        {
            Guid first = Guid.NewGuid();
            Guid second = Guid.NewGuid();

            await this.SendAsync(
                new[]
                {
                    PushRecipient.ForUser(first, "tok-1", Languages.English),
                    PushRecipient.ForUser(second, "tok-2", Languages.English),
                },
                new { type = "tournamentWon", tournamentId = "t-1" });

            var sent = this.Sent();
            string? firstId = NotificationIdOf(sent.Single(m => m.To == "tok-1"));
            string? secondId = NotificationIdOf(sent.Single(m => m.To == "tok-2"));

            // Tapping the push marks THAT user's row read, so the ids must not be shared — this is the
            // one thing a per-language payload (built once per batch) would quietly get wrong.
            Assert.That(firstId, Is.EqualTo((await this.RowsFor(first)).Single().Id!.Value.ToString()));
            Assert.That(secondId, Is.EqualTo((await this.RowsFor(second)).Single().Id!.Value.ToString()));
            Assert.That(firstId, Is.Not.EqualTo(secondId));
        }

        [Test]
        public async Task TheRestOfThePayloadSurvivesTheInjection()
        {
            Guid userId = Guid.NewGuid();

            await this.SendAsync(
                new[] { PushRecipient.ForUser(userId, "tok", Languages.English) },
                new { type = "checkIn", tournamentId = "t-1", matchId = "m-1" });

            JsonElement data = this.Sent().Single().Data!.Value;

            // The router reads these; losing one would land the tap on the wrong screen.
            Assert.That(data.GetProperty("type").GetString(), Is.EqualTo("checkIn"));
            Assert.That(data.GetProperty("tournamentId").GetString(), Is.EqualTo("t-1"));
            Assert.That(data.GetProperty("matchId").GetString(), Is.EqualTo("m-1"));
            Assert.That(data.TryGetProperty("notificationId", out _), Is.True);
        }

        [Test]
        public async Task ARecipientWithoutAUserId_GetsNoRowAndAnUntouchedPayload()
        {
            await this.SendAsync(
                new[] { new PushRecipient("tok-legacy", Languages.English) },
                new { type = "tournamentWon", tournamentId = "t-1" });

            JsonElement data = this.Sent().Single().Data!.Value;

            Assert.That(data.TryGetProperty("notificationId", out _), Is.False);
            Assert.That(await this.context.Set<NotificationEntity>().CountAsync(), Is.Zero);
        }

        [Test]
        public async Task OneRowPerUser_EvenWhenTheyAreSignedInOnSeveralDevices()
        {
            Guid userId = Guid.NewGuid();

            await this.SendAsync(new[]
            {
                PushRecipient.ForUser(userId, "phone", Languages.English),
                PushRecipient.ForUser(userId, "tablet", Languages.English),
            });

            Assert.That(await this.RowsFor(userId), Has.Count.EqualTo(1), "the inbox is per account, not per device");
            Assert.That(this.Sent(), Has.Count.EqualTo(2), "but both devices still get the push");

            // Both devices point at the same row, so reading it on one clears it on the other.
            Assert.That(this.Sent().Select(NotificationIdOf).Distinct().Count(), Is.EqualTo(1));
        }

        // ── Classification ───────────────────────────────────────────────────────────────────────

        // Every type string the codebase actually sends, taken from the `type = "…"` payload literals.
        // A typo on either side of this table — call site or ActionTypes — puts a notification in the
        // wrong tab, and nothing else would catch it.
        private static readonly string[] ActionTypes =
        {
            "checkIn", "roundDeadline", "opponentReady", "resultProposed", "teamTieBreak",
            "adminHelp", "hubJoinRequest", "teamJoinRequest", "friend_request", "scheduleCleared",
            "matchAvailability",
        };

        private static readonly string[] UpdateTypes =
        {
            "tournamentWon", "tournamentLive", "registrationOpen", "registrationScheduled",
            "registrationDeadline", "participantSwappedIn", "participantSwappedOut",
            "teamLineupIn", "teamLineupOut", "teamJoinApproved", "teamJoinRejected",
            "hubJoinApproved", "hubJoinRejected", "friend_accepted", "adminHelpResolved",
            "matchScheduled",
        };

        [Test]
        public async Task ActionTypes_LandInTheNeedsYouTab([ValueSource(nameof(ActionTypes))] string type)
        {
            Guid userId = Guid.NewGuid();

            await this.SendAsync(new[] { PushRecipient.ForUser(userId, "tok", Languages.English) }, new { type });

            Assert.That((await this.RowsFor(userId)).Single().Category, Is.EqualTo(NotificationCategory.Action));
        }

        [Test]
        public async Task EverythingElse_IsAnUpdate([ValueSource(nameof(UpdateTypes))] string type)
        {
            Guid userId = Guid.NewGuid();

            await this.SendAsync(new[] { PushRecipient.ForUser(userId, "tok", Languages.English) }, new { type });

            Assert.That((await this.RowsFor(userId)).Single().Category, Is.EqualTo(NotificationCategory.Update));
        }

        [Test]
        public async Task AnUntypedPayload_IsAnUpdate_AndKeepsANullType()
        {
            Guid userId = Guid.NewGuid();

            // Several backend pushes carry only ids; the app routes them by the id ladder.
            await this.SendAsync(
                new[] { PushRecipient.ForUser(userId, "tok", Languages.English) },
                new { tournamentId = "t-1" });

            var row = (await this.RowsFor(userId)).Single();

            Assert.That(row.Category, Is.EqualTo(NotificationCategory.Update));
            Assert.That(row.Type, Is.Null);
        }

        [Test]
        public async Task ChatTypes_AreNeverActions()
        {
            Guid userId = Guid.NewGuid();

            // Chat never reaches this path at all — DirectChatService and MatchChatService deliberately
            // stay on SendToOneAsync, which writes no row, because chat has its own unread surfaces. This
            // pins the fallback: if either is ever moved onto the localized API for translated push copy,
            // the messages must not start crowding the "Needs you" tab.
            foreach (string type in new[] { "direct_message", "matchMessage" })
            {
                await this.SendAsync(new[] { PushRecipient.ForUser(userId, "tok", Languages.English) }, new { type });
            }

            Assert.That(
                (await this.RowsFor(userId)).Select(r => r.Category),
                Is.All.EqualTo(NotificationCategory.Update));
        }

        // ── Counters ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public async Task TheSummarySplitsUnreadByCategory()
        {
            Guid userId = Guid.NewGuid();

            await this.SendAsync(new[] { PushRecipient.ForUser(userId, "tok", Languages.English) }, new { type = "checkIn" });
            await this.SendAsync(new[] { PushRecipient.ForUser(userId, "tok", Languages.English) }, new { type = "adminHelp" });
            await this.SendAsync(new[] { PushRecipient.ForUser(userId, "tok", Languages.English) }, new { type = "tournamentWon" });

            var summary = (await this.unitOfWork.NotificationRepository.GetSummaries(new[] { userId }))[userId];

            Assert.Multiple(() =>
            {
                Assert.That(summary.UnreadActions, Is.EqualTo(2));
                Assert.That(summary.UnreadUpdates, Is.EqualTo(1));
                Assert.That(summary.Unread, Is.EqualTo(3));
                Assert.That(summary.Unseen, Is.EqualTo(3), "nothing has been opened yet, so the bell shows all three");
            });
        }

        [Test]
        public async Task ANotificationForSeveralUsers_CountsOncePerUser()
        {
            Guid first = Guid.NewGuid();
            Guid second = Guid.NewGuid();

            await this.SendAsync(
                new[]
                {
                    PushRecipient.ForUser(first, "tok-1", Languages.English),
                    PushRecipient.ForUser(second, "tok-2", Languages.Spanish),
                },
                new { type = "roundDeadline" });

            var summaries = await this.unitOfWork.NotificationRepository.GetSummaries(new[] { first, second });

            Assert.That(summaries[first].UnreadActions, Is.EqualTo(1));
            Assert.That(summaries[second].UnreadActions, Is.EqualTo(1));
        }

        // ── The swallowed failure ────────────────────────────────────────────────────────────────

        [Test]
        public async Task AFailingInbox_NeverStopsThePush()
        {
            this.inboxThrows = true;

            await this.SendAsync(
                new[] { PushRecipient.ForUser(Guid.NewGuid(), "tok", Languages.English) },
                new { type = "checkIn", matchId = "m-1" });

            var sent = this.Sent();

            // The contract the try/catch in NotificationService.RecordInInboxAsync exists for: losing the
            // inbox row is bad, not telling someone their match is starting is worse.
            Assert.That(sent, Has.Count.EqualTo(1));
            Assert.That(sent[0].Title, Is.EqualTo(TitleEn));

            // And the payload degrades to exactly what a pre-inbox build sent, so the tap still routes.
            JsonElement data = sent[0].Data!.Value;
            Assert.That(data.GetProperty("matchId").GetString(), Is.EqualTo("m-1"));
            Assert.That(data.TryGetProperty("notificationId", out _), Is.False);
        }
    }
}
