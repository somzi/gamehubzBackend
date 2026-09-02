using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;
using NUnit.Framework;

using GameHubz.DataModels.Consts;
using GameHubz.Localization;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;

namespace GameHubz.Logic.Test.Services
{
    /// <summary>
    /// A push is read by the RECIPIENT, so its wording comes from their profile language and not
    /// from whoever triggered it. These tests capture the exact payloads that would go to Expo:
    /// the grouping, the 100-per-request chunking and the token de-duplication are only ever
    /// exercised at runtime otherwise, and a mistake there is silent — the wrong language, or a
    /// batch quietly rejected for being oversized.
    /// </summary>
    [TestFixture]
    internal sealed class NotificationServiceLocalizationTests
    {
        // Real resource strings, so a mismatch between key and resx shows up here too.
        private const string TitleKey = "Push.ResultToConfirm.Title";
        private const string TitleEn = "Result to confirm";
        private const string TitleEs = "Resultado por confirmar";
        private const string BodyKey = "Push.TournamentWon.Body";

        /// <summary>Records every batch posted to Expo instead of sending it.</summary>
        private sealed class CapturingHandler : HttpMessageHandler
        {
            public List<List<CapturedMessage>> Batches { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string json = await request.Content!.ReadAsStringAsync(cancellationToken);
                Batches.Add(JsonSerializer.Deserialize<List<CapturedMessage>>(
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
        }

        private static (NotificationService Service, CapturingHandler Handler) Build()
        {
            var handler = new CapturingHandler();

            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>()))
                   .Returns(() => new HttpClient(handler, disposeHandler: false));

            // No HttpContext: a push is always resolved through the explicit-language overload,
            // never through the request header. Passing null proves we never depend on one.
            var localization = new LocalizationService(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Language"] = Languages.English })
                    .Build(),
                httpContextAccessor: null);

            var service = new NotificationService(
                factory.Object,
                NullLogger<NotificationService>.Instance,
                new Mock<IServiceScopeFactory>().Object,
                localization);

            return (service, handler);
        }

        private static List<CapturedMessage> AllMessages(CapturingHandler handler)
            => handler.Batches.SelectMany(b => b).ToList();

        [Test]
        public async Task EachRecipientGetsTheirOwnLanguage()
        {
            var (service, handler) = Build();

            await service.SendLocalizedToManyAsync(
                new[]
                {
                    new PushRecipient("tok-es", Languages.Spanish),
                    new PushRecipient("tok-en", Languages.English),
                },
                PushText.FromKey(TitleKey),
                PushText.FromKey(BodyKey));

            var sent = AllMessages(handler);

            Assert.That(sent, Has.Count.EqualTo(2));
            Assert.That(sent.Single(m => m.To == "tok-es").Title, Is.EqualTo(TitleEs));
            Assert.That(sent.Single(m => m.To == "tok-en").Title, Is.EqualTo(TitleEn));
        }

        [Test]
        public async Task RegionalTagIsNormalised_AndSharesTheSpanishBatch()
        {
            var (service, handler) = Build();

            await service.SendLocalizedToManyAsync(
                new[]
                {
                    new PushRecipient("tok-a", "es-419"),
                    new PushRecipient("tok-b", "ES"),
                },
                PushText.FromKey(TitleKey),
                PushText.FromKey(BodyKey));

            Assert.That(AllMessages(handler).Select(m => m.Title),
                Is.All.EqualTo(TitleEs));
        }

        [Test]
        public async Task NullLanguage_FallsBackToConfigured_NotToAnEmptyString()
        {
            var (service, handler) = Build();

            await service.SendLocalizedToManyAsync(
                new[] { new PushRecipient("tok", null) },
                PushText.FromKey(TitleKey),
                PushText.FromKey(BodyKey));

            Assert.That(AllMessages(handler).Single().Title, Is.EqualTo(TitleEn));
        }

        [Test]
        public async Task LiteralText_IsNeverTranslated()
        {
            var (service, handler) = Build();

            await service.SendLocalizedToManyAsync(
                new[] { new PushRecipient("tok", Languages.Spanish) },
                PushText.FromLiteral("Bojan's Cup"),
                PushText.FromKey(BodyKey));

            // Tournament and team names are data, not copy.
            Assert.That(AllMessages(handler).Single().Title, Is.EqualTo("Bojan's Cup"));
        }

        [Test]
        public async Task Arguments_AreFormattedInTheRecipientsLanguage()
        {
            var (service, handler) = Build();

            await service.SendLocalizedToManyAsync(
                new[]
                {
                    new PushRecipient("tok-es", Languages.Spanish),
                    new PushRecipient("tok-en", Languages.English),
                },
                PushText.FromKey(TitleKey),
                PushText.FromKey("Push.ResultToConfirm.Body", "Bojan"));

            var sent = AllMessages(handler);

            Assert.That(sent.Single(m => m.To == "tok-es").Body, Does.Contain("Bojan"));
            Assert.That(sent.Single(m => m.To == "tok-en").Body, Does.Contain("Bojan"));
            // Same argument, different sentence around it.
            Assert.That(sent.Single(m => m.To == "tok-es").Body,
                Is.Not.EqualTo(sent.Single(m => m.To == "tok-en").Body));
        }

        [Test]
        public async Task DuplicateToken_IsSentOnce()
        {
            var (service, handler) = Build();

            await service.SendLocalizedToManyAsync(
                new[]
                {
                    new PushRecipient("same", Languages.English),
                    new PushRecipient("same", Languages.English),
                },
                PushText.FromKey(TitleKey),
                PushText.FromKey(BodyKey));

            Assert.That(AllMessages(handler), Has.Count.EqualTo(1));
        }

        [Test]
        public async Task BlankTokens_AreSkipped_AndAnAllBlankListSendsNothing()
        {
            var (service, handler) = Build();

            await service.SendLocalizedToManyAsync(
                new[]
                {
                    new PushRecipient("", Languages.English),
                    new PushRecipient("   ", Languages.English),
                },
                PushText.FromKey(TitleKey),
                PushText.FromKey(BodyKey));

            Assert.That(handler.Batches, Is.Empty);
        }

        [Test]
        public async Task OverAHundredRecipients_AreSplitIntoExpoSizedBatches()
        {
            var (service, handler) = Build();

            var recipients = Enumerable.Range(0, 250)
                .Select(i => new PushRecipient($"tok-{i}", Languages.English))
                .ToList();

            await service.SendLocalizedToManyAsync(
                recipients, PushText.FromKey(TitleKey), PushText.FromKey(BodyKey));

            // Expo rejects a request carrying more than 100 notifications.
            Assert.That(handler.Batches.Select(b => b.Count), Is.EqualTo(new[] { 100, 100, 50 }));
            Assert.That(AllMessages(handler).Select(m => m.To).Distinct().Count(), Is.EqualTo(250));
        }

        [Test]
        public async Task ChunkingIsPerLanguage_SoNoBatchMixesTwoLanguages()
        {
            var (service, handler) = Build();

            var recipients = Enumerable.Range(0, 120)
                .Select(i => new PushRecipient(
                    $"tok-{i}", i % 2 == 0 ? Languages.Spanish : Languages.English))
                .ToList();

            await service.SendLocalizedToManyAsync(
                recipients, PushText.FromKey(TitleKey), PushText.FromKey(BodyKey));

            foreach (var batch in handler.Batches)
            {
                Assert.That(batch.Select(m => m.Title).Distinct().Count(), Is.EqualTo(1),
                    "a single Expo request must carry one language only");
            }

            Assert.That(AllMessages(handler), Has.Count.EqualTo(120));
        }

        [Test]
        public async Task SendLocalizedToOne_UsesTheSamePath()
        {
            var (service, handler) = Build();

            await service.SendLocalizedToOneAsync(
                new PushRecipient("tok", Languages.Spanish),
                PushText.FromKey(TitleKey),
                PushText.FromKey(BodyKey));

            Assert.That(AllMessages(handler).Single().Title, Is.EqualTo(TitleEs));
        }
    }
}
