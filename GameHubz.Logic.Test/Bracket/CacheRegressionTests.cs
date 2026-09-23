using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

using GameHubz.Common.Interfaces;
using GameHubz.Common.Models;
using GameHubz.Data.Context;
using GameHubz.DataModels.Config;
using GameHubz.DataModels.Consts;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Localization;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;

namespace GameHubz.Logic.Test.Bracket
{
    [TestFixture]
    internal sealed class CacheRegressionTests
    {
        [Test]
        public async Task BracketCache_IsPartitionedByLanguage_AndV3Shape()
        {
            var tournamentId = Guid.NewGuid();
            var cache = new FakeCacheService();
            var englishV1 = new TournamentStructureDto { Name = "English v1" };
            var spanishV1 = new TournamentStructureDto { Name = "Spanish v1" };
            var englishV3 = new TournamentStructureDto { Name = "English v3" };

            await cache.SetAsync($"bracket:{tournamentId}:{Languages.English}", englishV1);
            await cache.SetAsync($"bracket:{tournamentId}:{Languages.Spanish}", spanishV1);
            await cache.SetAsync($"bracket:v3:{tournamentId}:{Languages.English}", englishV3);

            var english = BuildBracketService(cache, LocalizationFor(Languages.English));
            var spanish = BuildBracketService(cache, LocalizationFor(Languages.Spanish));

            var actualEnglishV1 = await english.GetTournamentStructure(tournamentId);
            var actualSpanishV1 = await spanish.GetTournamentStructure(tournamentId);
            var actualEnglishV3 = await english.GetTournamentStructureV3(tournamentId);

            Assert.Multiple(() =>
            {
                Assert.That(actualEnglishV1.Name, Is.EqualTo("English v1"));
                Assert.That(actualSpanishV1.Name, Is.EqualTo("Spanish v1"));
                Assert.That(actualEnglishV3.Name, Is.EqualTo("English v3"));
            });
        }

        [Test]
        public async Task PdfCache_IsPartitionedByLanguage_AndScheduleVariant()
        {
            var tournamentId = Guid.NewGuid();
            var cache = new FakeCacheService();
            byte[] englishStandard = { 1 };
            byte[] spanishStandard = { 2 };
            byte[] englishSchedule = { 3 };

            await cache.SetAsync($"pdf:bracket:{tournamentId}:{Languages.English}:std", englishStandard);
            await cache.SetAsync($"pdf:bracket:{tournamentId}:{Languages.Spanish}:std", spanishStandard);
            await cache.SetAsync($"pdf:bracket:{tournamentId}:{Languages.English}:sched", englishSchedule);
            await cache.SetAsync($"pdf:bracket:name:{tournamentId}", "Tournament");

            var english = new TournamentExportService(
                bracketService: null!, cache, LocalizationFor(Languages.English));
            var spanish = new TournamentExportService(
                bracketService: null!, cache, LocalizationFor(Languages.Spanish));

            var actualEnglishStandard = await english.GenerateBracketPdfAsync(tournamentId);
            var actualSpanishStandard = await spanish.GenerateBracketPdfAsync(tournamentId);
            var actualEnglishSchedule = await english.GenerateBracketPdfAsync(
                tournamentId, includeSchedule: true);

            Assert.Multiple(() =>
            {
                Assert.That(actualEnglishStandard.Pdf, Is.SameAs(englishStandard));
                Assert.That(actualSpanishStandard.Pdf, Is.SameAs(spanishStandard));
                Assert.That(actualEnglishSchedule.Pdf, Is.SameAs(englishSchedule));
            });
        }

        [Test]
        public async Task RoundScheduleChange_InvalidatesEveryPdfLanguageAndVariant()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tournamentId);

            string englishSchedule = $"pdf:bracket:{tournamentId}:{Languages.English}:sched";
            string spanishStandard = $"pdf:bracket:{tournamentId}:{Languages.Spanish}:std";
            await harness.Cache.SetAsync(englishSchedule, new byte[] { 1 });
            await harness.Cache.SetAsync(spanishStandard, new byte[] { 2 });

            await harness.NewTournamentService().SetRoundDeadline(
                tournamentId,
                roundNumber: 1,
                deadline: DateTime.UtcNow.AddDays(2),
                roundStart: DateTime.UtcNow.AddDays(1));

            var cachedEnglishSchedule = await harness.Cache.GetAsync<byte[]>(englishSchedule);
            var cachedSpanishStandard = await harness.Cache.GetAsync<byte[]>(spanishStandard);

            Assert.Multiple(() =>
            {
                Assert.That(cachedEnglishSchedule, Is.Null);
                Assert.That(cachedSpanishStandard, Is.Null);
                Assert.That(harness.Cache.RemovedPatterns,
                    Does.Contain($"pdf:bracket:{tournamentId}:*"));
            });
        }

        private static BracketService BuildBracketService(
            FakeCacheService cache,
            ILocalizationService localization)
        {
            var options = new DbContextOptionsBuilder<ApplicationContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var factory = new TestUnitOfWorkFactory(new ApplicationContext(options), localization);
            var userContext = new Mock<IUserContextReader>();
            userContext.Setup(reader => reader.GetTokenUserInfoFromContext())
                .Returns(Task.FromResult<TokenUserInfo?>(null));

            return new BracketService(
                factory,
                userContext.Object,
                localization,
                hubActivityService: null!,
                cache,
                notificationService: null!,
                tournamentAuth: null!,
                badgeService: null!,
                tournamentNotifier: null!,
                matchNotifier: null!,
                bracketNotifier: null!,
                discordDmService: null!,
                Options.Create(new ShareLinksConfig()),
                chatAccessRevoker: null!);
        }

        private static ILocalizationService LocalizationFor(string language)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Language"] = language,
                })
                .Build();
            return new LocalizationService(configuration);
        }
    }
}
