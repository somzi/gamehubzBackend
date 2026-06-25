using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Test.Bracket
{
    [TestFixture]
    internal sealed class SingleEliminationGenerationTests
    {
        // Power-of-two fields: a full bracket of N players has exactly N-1 matches, no byes.
        [TestCase(2, 1)]
        [TestCase(4, 2)]
        [TestCase(8, 3)]
        [TestCase(16, 4)]
        [TestCase(32, 5)]
        public async Task PowerOfTwo_HasNMinusOneMatchesAndCorrectRoundShape(int players, int expectedRounds)
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, players);

            await harness.Service.GenerateSingleEliminationBracket(tournamentId);

            var matches = harness.Matches(tournamentId);

            Assert.That(matches.Count, Is.EqualTo(players - 1), "single elim should have N-1 matches");
            Assert.That(matches.Select(m => m.RoundNumber).Max(), Is.EqualTo(expectedRounds), "round count");
            Assert.That(matches.Count(m => m.RoundNumber == 1), Is.EqualTo(players / 2), "first round match count");
            Assert.That(matches.Count(m => m.Stage == MatchStage.Final), Is.EqualTo(1), "exactly one final");
            Assert.That(matches.Count(m => m.Status == MatchStatus.Completed), Is.EqualTo(0), "no byes for power-of-two");

            var round1 = matches.Where(m => m.RoundNumber == 1).ToList();
            Assert.That(round1.All(m => m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue), Is.True,
                "all first-round slots filled");
        }

        // Non-power-of-two: the bracket pads up to the next power of two; the surplus slots become
        // byes that are auto-advanced (Completed with a winner) at generation time.
        [TestCase(3)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(9)]
        [TestCase(12)]
        public async Task NonPowerOfTwo_ByesAreAutoAdvanced(int players)
        {
            int bracketSize = BracketTestHarness.NextPowerOfTwo(players);
            int expectedByes = bracketSize - players;

            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, players);

            await harness.Service.GenerateSingleEliminationBracket(tournamentId);

            var matches = harness.Matches(tournamentId);

            Assert.That(matches.Count, Is.EqualTo(bracketSize - 1), "tree size is bracketSize-1 regardless of byes");

            var completed = matches.Where(m => m.Status == MatchStatus.Completed).ToList();
            Assert.That(completed.Count, Is.EqualTo(expectedByes), "one auto-advanced bye per surplus slot");

            foreach (var bye in completed)
            {
                bool exactlyOne = bye.HomeParticipantId.HasValue ^ bye.AwayParticipantId.HasValue;
                Assert.That(exactlyOne, Is.True, "a bye has exactly one real participant");
                var present = bye.HomeParticipantId ?? bye.AwayParticipantId;
                Assert.That(bye.WinnerParticipantId, Is.EqualTo(present), "bye winner is the lone participant");
            }

            // The bye winners are pushed into their next match's slots, so round 2 already has
            // some participants pre-filled.
            var round2WithParticipant = matches.Count(m => m.RoundNumber == 2 &&
                (m.HomeParticipantId.HasValue || m.AwayParticipantId.HasValue));
            Assert.That(round2WithParticipant, Is.GreaterThan(0), "bye winners advanced into round 2");
        }

        [Test]
        public async Task EveryNonFinalMatchFeedsExactlyOneNextMatch()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 8);

            await harness.Service.GenerateSingleEliminationBracket(tournamentId);

            var matches = harness.Matches(tournamentId);
            int maxRound = matches.Select(m => m.RoundNumber ?? 0).Max();

            foreach (var match in matches.Where(m => m.RoundNumber < maxRound))
            {
                Assert.That(match.NextMatchId.HasValue, Is.True, "non-final match must feed a next match");
            }

            foreach (var group in matches.Where(m => m.NextMatchId.HasValue).GroupBy(m => m.NextMatchId))
            {
                Assert.That(group.Count(), Is.EqualTo(2), "each next match is fed by exactly two source matches");
            }
        }

        [TestCase(4)]
        [TestCase(8)]
        public async Task ThirdPlaceMatch_IsCreatedWhenRequested(int players)
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.SingleElimination, players, hasThirdPlaceMatch: true);

            await harness.Service.GenerateSingleEliminationBracket(tournamentId);

            var matches = harness.Matches(tournamentId);

            Assert.That(matches.Count(m => m.Stage == MatchStage.ThirdPlace), Is.EqualTo(1),
                "exactly one third-place play-off");
            Assert.That(matches.Count, Is.EqualTo(players - 1 + 1), "tree + third-place match");

            // Both semi-finals point their loser pointer at the third-place match.
            var thirdPlace = matches.Single(m => m.Stage == MatchStage.ThirdPlace);
            var semis = matches.Where(m => m.Stage == MatchStage.SemiFinal).ToList();
            Assert.That(semis.Count, Is.EqualTo(2), "two semi-finals");
            Assert.That(semis.All(s => s.NextMatchLoserBracketId == thirdPlace.Id), Is.True,
                "both semi-final losers feed the third-place match");
        }

        [Test]
        public async Task NoParticipants_Throws()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 0);

            Assert.That(async () => await harness.Service.GenerateSingleEliminationBracket(tournamentId),
                Throws.Exception);
        }
    }
}
