using NUnit.Framework;

using GameHubz.Logic.Services;

namespace GameHubz.Logic.Test.Services
{
    /// <summary>
    /// The per-match clip cap follows the series: two clips per game, one for each side. The count
    /// it is compared against includes the per-game verification recordings, which is why a fixed
    /// cap of three left no room for an ordinary clip in a Best-of-3.
    /// </summary>
    [TestFixture]
    internal sealed class EvidenceVideoLimitTests
    {
        [TestCase(2, 4)]
        [TestCase(3, 6)]
        [TestCase(5, 10)]
        public void Cap_IsTwoClipsPerGameOfTheSeries(int bestOf, int expected)
        {
            Assert.That(MatchService.VideoLimitFor(bestOf, tiebreakBestOf: null, lastSeries: 1), Is.EqualTo(expected));
        }

        [Test]
        public void SingleGameMatch_KeepsTheThreeClipsItAlwaysHad()
        {
            // Two per game would be two; nothing that uploads today should start being refused.
            Assert.That(MatchService.VideoLimitFor(bestOf: 1, tiebreakBestOf: null, lastSeries: 1), Is.EqualTo(3));
        }

        [Test]
        public void TiebreakReplay_BringsItsOwnGames()
        {
            Assert.Multiple(() =>
            {
                // Bo3, then a single-game tiebreak: 3 + 1 games.
                Assert.That(MatchService.VideoLimitFor(bestOf: 3, tiebreakBestOf: 1, lastSeries: 2), Is.EqualTo(8));
                // No distinct tiebreak format: a drawn Bo2 replays as another Bo2.
                Assert.That(MatchService.VideoLimitFor(bestOf: 2, tiebreakBestOf: null, lastSeries: 2), Is.EqualTo(8));
            });
        }
    }
}
