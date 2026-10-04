using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameHubz.Common.Interfaces;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;
using GameHubz.Logic.SignalR;
using GameHubz.Logic.Test.Bracket;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Services
{
    [TestFixture]
    internal sealed class NotificationSourceMuteTests
    {
        private SqliteConnection connection = null!;
        private TestApplicationContext context = null!;
        private IUserRepository users = null!;
        private IAppUnitOfWork uow = null!;
        private ServiceProvider services = null!;
        private NotificationService notifications = null!;
        private DiscordDmService discord = null!;
        private CapturingHandler handler = null!;
        private UserEntity muted = null!;
        private UserEntity allowed = null!;
        private HubEntity hub = null!;
        private TournamentEntity tournament = null!;
        private MatchEntity match = null!;
        private TeamMatchEntity teamMatch = null!;

        [SetUp]
        public async Task SetUp()
        {
            connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            context = new TestApplicationContext(new DbContextOptionsBuilder<TestApplicationContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            muted = new UserEntity { Id = Guid.NewGuid(), Username = "Muted", PushToken = "muted-token", DiscordUserId = "muted-discord" };
            allowed = new UserEntity { Id = Guid.NewGuid(), Username = "Allowed", PushToken = "allowed-token" };
            hub = new HubEntity { Id = Guid.NewGuid(), Name = "My hub", UserId = Guid.NewGuid() };
            tournament = new TournamentEntity { Id = Guid.NewGuid(), Name = "My tournament", Hub = hub, Status = TournamentStatus.InProgress };
            match = new MatchEntity { Id = Guid.NewGuid(), Tournament = tournament };
            teamMatch = new TeamMatchEntity { Id = Guid.NewGuid(), Tournament = tournament };
            context.AddRange(muted, allowed, match, teamMatch);
            await context.SaveChangesAsync();

            var localization = new Mock<ILocalizationService>().Object;
            var realFactory = new TestUnitOfWorkFactory(context, localization);
            uow = realFactory.CreateAppUnitOfWork();
            users = uow.UserRepository;
            // Delivery normally owns a new scope/context. Keep the SQLite context open for assertions.
            var deliveryUow = new Mock<IAppUnitOfWork>();
            deliveryUow.SetupGet(u => u.UserRepository).Returns(users);
            var deliveryFactory = new Mock<IUnitOfWorkFactory>();
            deliveryFactory.Setup(f => f.CreateAppUnitOfWork()).Returns(deliveryUow.Object);
            var inbox = new NotificationInboxService(realFactory, new Mock<IUserContextReader>().Object,
                localization, new Mock<IHubContext<UserHub>>().Object);
            services = new ServiceCollection().AddSingleton(deliveryFactory.Object).AddSingleton(inbox).BuildServiceProvider();
            handler = new CapturingHandler();
            var http = new Mock<IHttpClientFactory>();
            http.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() =>
            {
                var client = new HttpClient(handler, false) { BaseAddress = new Uri("https://discord.invalid/") };
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", "test-token");
                return client;
            });
            notifications = new NotificationService(http.Object, NullLogger<NotificationService>.Instance,
                services.GetRequiredService<IServiceScopeFactory>(), localization);
            discord = new DiscordDmService(http.Object, NullLogger<DiscordDmService>.Instance,
                services.GetRequiredService<IServiceScopeFactory>());
        }

        [TearDown]
        public void TearDown()
        {
            services.Dispose();
            context.Dispose();
            connection.Dispose();
            handler.Dispose();
        }

        private async Task MuteHub()
        {
            new NotificationSettingsDto { MutedHubIds = new() { hub.Id!.Value } }.ApplyTo(muted);
            await context.SaveChangesAsync();
        }

        private Task Send(object data) => notifications.SendLocalizedToManyAsync(new[]
        {
            PushRecipient.ForUser(muted.Id!.Value, muted.PushToken, "en"),
            PushRecipient.ForUser(allowed.Id!.Value, allowed.PushToken, "en"),
        }, PushText.FromLiteral("Title"), PushText.FromLiteral("Body"), data);

        [TestCase("hub")]
        [TestCase("tournament")]
        [TestCase("match")]
        [TestCase("teamMatch")]
        public async Task HubMuteFiltersEveryDescendantBeforePushAndInbox(string scope)
        {
            await MuteHub();
            object data = scope switch
            {
                "hub" => new { hubId = hub.Id },
                "tournament" => new { tournamentId = tournament.Id },
                "match" => new { matchId = match.Id },
                _ => new { teamMatchId = teamMatch.Id },
            };
            await Send(data);
            Assert.That(handler.PushTokens, Is.EqualTo(new[] { allowed.PushToken }));
            var rows = await context.Set<NotificationEntity>().ToListAsync();
            Assert.That(rows.Select(r => r.UserId), Is.EqualTo(new[] { allowed.Id!.Value }));
        }

        [Test]
        public async Task TournamentMuteDoesNotMuteSiblingTournamentOrHub()
        {
            new NotificationSettingsDto { MutedTournamentIds = new() { tournament.Id!.Value } }.ApplyTo(muted);
            var sibling = new TournamentEntity { Id = Guid.NewGuid(), Name = "Sibling", Hub = hub };
            context.Add(sibling);
            await context.SaveChangesAsync();
            await Send(new { tournamentId = tournament.Id });
            await Send(new { tournamentId = sibling.Id });
            await Send(new { hubId = hub.Id });
            Assert.That(handler.PushTokens.Count(t => t == muted.PushToken), Is.EqualTo(2));
            Assert.That(await context.Set<NotificationEntity>().CountAsync(n => n.UserId == muted.Id), Is.EqualTo(2));
        }

        [Test]
        public async Task TokenlessUsersAreFilteredFromInboxToo()
        {
            muted.PushToken = null;
            await MuteHub();
            await Send(new { tournamentId = tournament.Id });
            Assert.That(await context.Set<NotificationEntity>().CountAsync(n => n.UserId == muted.Id), Is.Zero);
            Assert.That(await context.Set<NotificationEntity>().CountAsync(n => n.UserId == allowed.Id), Is.EqualTo(1));
        }

        [Test]
        public async Task SharedDeviceTokenDoesNotMuteAnotherAccountsInbox()
        {
            allowed.PushToken = muted.PushToken;
            await MuteHub();
            await Send(new { tournamentId = tournament.Id });
            Assert.That(handler.PushTokens, Has.Count.EqualTo(1));
            Assert.That(await context.Set<NotificationEntity>().CountAsync(n => n.UserId == muted.Id), Is.Zero);
            Assert.That(await context.Set<NotificationEntity>().CountAsync(n => n.UserId == allowed.Id), Is.EqualTo(1));
        }

        [Test]
        public async Task UnmutingOnlyAHubPreservesExplicitTournamentMute()
        {
            await MuteHub();
            new NotificationSettingsDto { MutedTournamentIds = new() { tournament.Id!.Value } }.ApplyTo(muted);
            new NotificationSettingsDto { MutedHubIds = new() }.ApplyTo(muted);
            await context.SaveChangesAsync();
            await Send(new { tournamentId = tournament.Id });
            Assert.That(handler.PushTokens, Does.Not.Contain(muted.PushToken));
            new NotificationSettingsDto { MutedTournamentIds = new() }.ApplyTo(muted);
            await context.SaveChangesAsync();
            await Send(new { tournamentId = tournament.Id });
            Assert.That(handler.PushTokens, Does.Contain(muted.PushToken));
        }

        [Test]
        public async Task LegacySettingsUpdatePreservesSourcePreferences()
        {
            await MuteHub();
            var request = JsonSerializer.Deserialize<NotificationSettingsDto>("{\"ModeratedChatNotifications\":false}")!;
            request.ApplyTo(muted);
            await context.SaveChangesAsync();
            var stored = NotificationSettingsDto.FromUser((await context.Set<UserEntity>().AsNoTracking().SingleAsync(u => u.Id == muted.Id)));
            Assert.That(stored.ModeratedChatNotifications, Is.False);
            Assert.That(stored.MutedHubIds, Is.EqualTo(new[] { hub.Id!.Value }));
            Assert.That(stored.MutedTournamentIds, Is.Empty);
        }

        [Test]
        public async Task RawChatPushAndDiscordDmRespectSourceMutes()
        {
            await MuteHub();
            await notifications.SendToOneAsync(muted.PushToken!, "Title", "Body", new { matchId = match.Id });
            await notifications.SendToManyAsync(new[] { muted.PushToken!, allowed.PushToken! }, "Title", "Body", new { tournamentId = tournament.Id });
            await discord.SendDmAsync(muted.DiscordUserId!, "Body", tournament.Id);
            Assert.That(handler.PushTokens, Is.EqualTo(new[] { allowed.PushToken }));
            Assert.That(handler.DiscordRequests, Is.Zero);
        }

        [Test]
        public async Task DirectMessagesAndUnrelatedNotificationsStillArrive()
        {
            await MuteHub();
            await notifications.SendToOneAsync(muted.PushToken!, "Title", "Body", new { type = "direct_message", chatId = Guid.NewGuid() });
            await Send(new { type = "friend_request" });
            await discord.SendDmAsync(muted.DiscordUserId!, "Direct message");
            Assert.That(handler.PushTokens.Count(t => t == muted.PushToken), Is.EqualTo(2));
            Assert.That(handler.DiscordRequests, Is.GreaterThan(0));
        }

        [Test]
        public async Task HubPickerIsPersonalSearchableAndPaged()
        {
            for (int i = 0; i < 22; i++)
                context.Add(new HubEntity { Id = Guid.NewGuid(), UserId = muted.Id!.Value, Name = $"Joined {i:00}" });
            context.Add(new HubEntity { Id = Guid.NewGuid(), UserId = allowed.Id!.Value, Name = "Joined hidden" });
            await context.SaveChangesAsync();
            var first = await users.GetNotificationSources(muted.Id!.Value, "hubs", 0, "JOINED");
            var second = await users.GetNotificationSources(muted.Id!.Value, "hubs", 1, "joined");
            Assert.That(first.Items, Has.Count.EqualTo(20));
            Assert.That(first.NextPage, Is.EqualTo(1));
            Assert.That(second.Items, Has.Count.EqualTo(2));
            Assert.That(second.NextPage, Is.Null);
            Assert.That(first.Items.Concat(second.Items).Select(i => i.Name), Does.Not.Contain("Joined hidden"));
        }

        [Test]
        public async Task TournamentPickerIncludesMemberAnnouncementsAndHidesUnpublishedDrafts()
        {
            context.Add(new UserHubEntity { Id = Guid.NewGuid(), UserId = muted.Id, Hub = hub, HubRole = HubRole.HubMember });
            context.Add(new TournamentEntity { Id = Guid.NewGuid(), Name = "Unpublished", Hub = hub, Status = TournamentStatus.Draft });
            context.Add(new TournamentEntity { Id = Guid.NewGuid(), Name = "Scheduled", Hub = hub, Status = TournamentStatus.Draft, RegistrationOpensAt = DateTime.UtcNow.AddDays(1) });
            context.Add(new TournamentEntity { Id = Guid.NewGuid(), Name = "Exclusive", Hub = hub, Status = TournamentStatus.RegistrationOpen, IsExclusive = true });
            await context.SaveChangesAsync();
            var page = await users.GetNotificationSources(muted.Id!.Value, "tournaments", 0, null);
            Assert.That(page.Items.Select(i => i.Name), Is.EquivalentTo(new[] { tournament.Name, "Scheduled" }));
        }

        [Test]
        public async Task TournamentPickerIncludesOwnPrivateTournamentWithoutHubMembership()
        {
            tournament.IsPrivate = true;
            context.Add(new TournamentParticipantEntity { Id = Guid.NewGuid(), UserId = muted.Id, Tournament = tournament });
            context.Add(new TournamentEntity { Id = Guid.NewGuid(), Name = "Unrelated", Hub = hub, IsPrivate = true });
            await context.SaveChangesAsync();
            var page = await users.GetNotificationSources(muted.Id!.Value, "tournaments", 0, null);
            Assert.That(page.Items.Select(i => i.Id), Is.EqualTo(new[] { tournament.Id!.Value }));
            Assert.That(page.Items[0].HubId, Is.EqualTo(hub.Id));
        }

        private sealed class CapturingHandler : HttpMessageHandler
        {
            public List<string> PushTokens { get; } = new();
            public int DiscordRequests { get; private set; }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri!.Host == "exp.host")
                {
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    PushTokens.AddRange(body.RootElement.EnumerateArray().Select(message => message.GetProperty("to").GetString()!));
                }
                else DiscordRequests++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[],\"id\":\"test-channel\"}", System.Text.Encoding.UTF8, "application/json") };
            }
        }
    }
}
