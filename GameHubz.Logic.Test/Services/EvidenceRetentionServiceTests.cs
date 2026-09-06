using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;
using NUnit.Framework;

using GameHubz.Common.Interfaces;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;

namespace GameHubz.Logic.Test.Services
{
    /// <summary>
    /// This service permanently destroys user data, and it does so unattended on a timer. A bug in
    /// here does not crash anything — it quietly deletes evidence that was still needed, or quietly
    /// keeps paying for files that should be gone. Neither shows up in a log anyone reads, so the
    /// rules are pinned here instead.
    /// </summary>
    [TestFixture]
    internal sealed class EvidenceRetentionServiceTests
    {
        private Mock<IMatchEvidenceRepository> repository = null!;
        private Mock<IAppUnitOfWork> unitOfWork = null!;
        private Mock<IStorageService> storage = null!;

        [SetUp]
        public void SetUp()
        {
            repository = new Mock<IMatchEvidenceRepository>(MockBehavior.Loose);
            unitOfWork = new Mock<IAppUnitOfWork>(MockBehavior.Loose);
            storage = new Mock<IStorageService>(MockBehavior.Loose);

            unitOfWork.SetupGet(x => x.MatchEvidenceRepository).Returns(repository.Object);

            storage.SetupGet(x => x.Provider).Returns(StorageProviderType.Cloudinary);
            storage
                .Setup(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<EvidenceMediaType>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }

        private EvidenceRetentionService CreateService(params (string Key, string Value)[] settings)
        {
            var factory = new Mock<IUnitOfWorkFactory>();
            factory.Setup(x => x.CreateAppUnitOfWork()).Returns(unitOfWork.Object);

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
                .Build();

            return new EvidenceRetentionService(
                factory.Object,
                storage.Object,
                new Mock<IUserContextReader>().Object,
                NullLogger<EvidenceRetentionService>.Instance,
                configuration);
        }

        private static MatchEvidenceEntity Video(string? storageKey = "hub/x/clip")
            => new()
            {
                Id = Guid.NewGuid(),
                MediaType = EvidenceMediaType.Video,
                StorageKey = storageKey,
                Url = "https://res.cloudinary.com/demo/video/upload/v1/hub/x/clip.mp4",
            };

        private void SetupExpired(params MatchEvidenceEntity[] rows)
        {
            repository
                .Setup(x => x.GetExpiredVideos(
                    It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(rows.ToList());
        }

        // ---------------------------------------------------------------- retention windows

        [Test]
        public async Task Sweep_DefaultWindows_AreThreeDaysAndThirtyDays()
        {
            // The agreed policy, pinned so it cannot drift silently: clips go three days after the
            // tournament ends, and an abandoned tournament's clips go at thirty days regardless.
            DateTime endedBefore = default;
            DateTime createdBefore = default;

            repository
                .Setup(x => x.GetExpiredVideos(
                    It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Callback<DateTime, DateTime, int, CancellationToken>((ended, created, _, _) =>
                {
                    endedBefore = ended;
                    createdBefore = created;
                })
                .ReturnsAsync(new List<MatchEvidenceEntity>());

            DateTime before = DateTime.UtcNow;
            await CreateService().RunRetentionSweepAsync();

            Assert.Multiple(() =>
            {
                Assert.That((before - endedBefore).TotalDays, Is.EqualTo(3).Within(0.01));
                Assert.That((before - createdBefore).TotalDays, Is.EqualTo(30).Within(0.01));
            });
        }

        [Test]
        public async Task Sweep_HonoursConfiguredWindowsAndBatchSize()
        {
            DateTime endedBefore = default;
            DateTime createdBefore = default;
            int batchSize = 0;

            repository
                .Setup(x => x.GetExpiredVideos(
                    It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Callback<DateTime, DateTime, int, CancellationToken>((ended, created, take, _) =>
                {
                    endedBefore = ended;
                    createdBefore = created;
                    batchSize = take;
                })
                .ReturnsAsync(new List<MatchEvidenceEntity>());

            DateTime before = DateTime.UtcNow;

            await CreateService(
                ("Evidence:VideoRetentionDaysAfterTournamentEnd", "7"),
                ("Evidence:VideoMaxAgeDays", "45"),
                ("Evidence:SweepBatchSize", "25")).RunRetentionSweepAsync();

            Assert.Multiple(() =>
            {
                Assert.That((before - endedBefore).TotalDays, Is.EqualTo(7).Within(0.01));
                Assert.That((before - createdBefore).TotalDays, Is.EqualTo(45).Within(0.01));
                Assert.That(batchSize, Is.EqualTo(25));
            });
        }

        [Test]
        public async Task Sweep_WithNothingExpired_TouchesNeitherStorageNorDatabase()
        {
            SetupExpired();

            int purged = await CreateService().RunRetentionSweepAsync();

            Assert.That(purged, Is.Zero);
            storage.Verify(
                x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<EvidenceMediaType>(), It.IsAny<CancellationToken>()),
                Times.Never);
            unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<bool>()), Times.Never);
        }

        // ---------------------------------------------------------------- the happy path

        [Test]
        public async Task Sweep_DeletesTheAsset_RetiresTheRow_AndSavesOnce()
        {
            var row = Video();
            SetupExpired(row);

            int purged = await CreateService().RunRetentionSweepAsync();

            Assert.That(purged, Is.EqualTo(1));
            storage.Verify(x => x.DeleteAsync("hub/x/clip", EvidenceMediaType.Video, It.IsAny<CancellationToken>()), Times.Once);
            repository.Verify(x => x.SoftDeleteEntity(row, It.IsAny<IUserContextReader>()), Times.Once);
            // One save for the batch, not one per row.
            unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<bool>()), Times.Once);
        }

        [Test]
        public async Task Sweep_RowWithoutStorageKey_IsStillRetired_WithoutCallingStorage()
        {
            // Rows written before the storage handle existed cannot address their file. They are
            // retired anyway: left live, they would sit at the head of the oldest-first sweep and
            // block every row behind them forever.
            var orphan = Video(storageKey: null);
            SetupExpired(orphan);

            int purged = await CreateService().RunRetentionSweepAsync();

            Assert.That(purged, Is.EqualTo(1));
            storage.Verify(
                x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<EvidenceMediaType>(), It.IsAny<CancellationToken>()),
                Times.Never);
            repository.Verify(x => x.SoftDeleteEntity(orphan, It.IsAny<IUserContextReader>()), Times.Once);
        }

        // ---------------------------------------------------------------- failure handling

        [Test]
        public async Task Sweep_WhenStorageDeleteFails_LeavesThatRowLive_AndPurgesTheRest()
        {
            // The row is the only handle we have on the file. Retiring it after a failed delete
            // would strand the asset in storage with nothing left able to find it, so a failure
            // must leave the row alone for the next sweep to retry.
            var doomed = Video("hub/x/breaks");
            var fine = Video("hub/x/works");
            SetupExpired(doomed, fine);

            storage
                .Setup(x => x.DeleteAsync("hub/x/breaks", It.IsAny<EvidenceMediaType>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("provider is down"));

            int purged = await CreateService().RunRetentionSweepAsync();

            Assert.That(purged, Is.EqualTo(1));
            repository.Verify(x => x.SoftDeleteEntity(doomed, It.IsAny<IUserContextReader>()), Times.Never);
            repository.Verify(x => x.SoftDeleteEntity(fine, It.IsAny<IUserContextReader>()), Times.Once);
            unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<bool>()), Times.Once);
        }

        [Test]
        public async Task Sweep_WhenEveryDeleteFails_SavesNothing()
        {
            SetupExpired(Video("a"), Video("b"));

            storage
                .Setup(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<EvidenceMediaType>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("provider is down"));

            int purged = await CreateService().RunRetentionSweepAsync();

            Assert.That(purged, Is.Zero);
            unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<bool>()), Times.Never);
        }

        // ---------------------------------------------------------------- immediate purge

        [Test]
        public async Task PurgeTournamentVideos_AsksForVideoOnly()
        {
            // Images are not on a retention clock at any point, including here. Cancelling a
            // tournament must not take the screenshots with it.
            var tournamentId = Guid.NewGuid();
            repository
                .Setup(x => x.GetByTournament(tournamentId, It.IsAny<EvidenceMediaType>()))
                .ReturnsAsync(new List<MatchEvidenceEntity> { Video() });

            await CreateService().PurgeTournamentVideosAsync(tournamentId);

            repository.Verify(x => x.GetByTournament(tournamentId, EvidenceMediaType.Video), Times.Once);
            repository.Verify(x => x.GetByTournament(tournamentId, EvidenceMediaType.Image), Times.Never);
        }

        [Test]
        public void PurgeTournamentVideos_WhenTheQueryFails_DoesNotPropagate()
        {
            // It runs as a side effect of cancelling a tournament. Throwing here would fail the
            // cancel itself over a storage problem, and the periodic sweep is already the retry.
            repository
                .Setup(x => x.GetByTournament(It.IsAny<Guid>(), It.IsAny<EvidenceMediaType>()))
                .ThrowsAsync(new Exception("database is unreachable"));

            var service = CreateService();

            Assert.DoesNotThrowAsync(async () =>
            {
                int purged = await service.PurgeTournamentVideosAsync(Guid.NewGuid());
                Assert.That(purged, Is.Zero);
            });
        }

        [Test]
        public async Task PurgeTournamentVideos_WithNothingToDrop_DoesNotSave()
        {
            repository
                .Setup(x => x.GetByTournament(It.IsAny<Guid>(), It.IsAny<EvidenceMediaType>()))
                .ReturnsAsync(new List<MatchEvidenceEntity>());

            int purged = await CreateService().PurgeTournamentVideosAsync(Guid.NewGuid());

            Assert.That(purged, Is.Zero);
            unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<bool>()), Times.Never);
        }

        // ---------------------------------------------------------------- shutdown

        [Test]
        public async Task Sweep_WhenCancelledMidBatch_KeepsWhatItAlreadyPurged()
        {
            // A deploy during a sweep must not lose the deletes it already performed: those files
            // are gone from storage, so their rows have to be retired or they become orphans.
            var first = Video("first");
            var second = Video("second");
            SetupExpired(first, second);

            using var cts = new CancellationTokenSource();

            storage
                .Setup(x => x.DeleteAsync("first", It.IsAny<EvidenceMediaType>(), It.IsAny<CancellationToken>()))
                .Callback(() => cts.Cancel())
                .ReturnsAsync(true);

            int purged = await CreateService().RunRetentionSweepAsync(cts.Token);

            Assert.That(purged, Is.EqualTo(1));
            repository.Verify(x => x.SoftDeleteEntity(first, It.IsAny<IUserContextReader>()), Times.Once);
            repository.Verify(x => x.SoftDeleteEntity(second, It.IsAny<IUserContextReader>()), Times.Never);
            unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<bool>()), Times.Once);
        }
    }
}
