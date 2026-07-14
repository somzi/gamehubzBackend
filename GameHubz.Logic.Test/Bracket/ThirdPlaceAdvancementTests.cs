using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;

namespace GameHubz.Logic.Test.Bracket
{
    // Third-place play-off advancement (generation shape is covered elsewhere): the semi-final
    // losers must meet in the play-off, and the title must wait until BOTH the final and the
    // play-off are played — with the champion always the final's winner. SQLite harness.
    [TestFixture]
    internal sealed class ThirdPlaceAdvancementTests
    {
        [Test]
        public async Task SemifinalLosersMeet_AndTitleWaitsForBothFinalAndPlayoff()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.SingleElimination, 4, hasThirdPlaceMatch: true);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var semis = harness.Matches(tournamentId).Where(m => m.RoundNumber == 1).ToList();
            Assert.That(semis.Count, Is.EqualTo(2));

            foreach (var semi in semis)
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = semi.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
                });
            }

            // Both losers (the away sides) landed in the play-off.
            var thirdPlace = harness.Matches(tournamentId).Single(m => m.Stage == MatchStage.ThirdPlace);
            var semiLosers = semis.Select(s => s.AwayParticipantId!.Value).ToHashSet();
            var playoffField = new[] { thirdPlace.HomeParticipantId, thirdPlace.AwayParticipantId }
                .Select(id => id!.Value)
                .ToHashSet();
            Assert.That(playoffField, Is.EquivalentTo(semiLosers), "semi-final losers meet for third place");

            // Final played, play-off still open -> the tournament must NOT complete yet.
            var final = harness.Matches(tournamentId).Single(m => m.Stage == MatchStage.Final);
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = final.Id!.Value, TournamentId = tournamentId, HomeScore = 3, AwayScore = 1,
            });
            Assert.That(harness.Tournament(tournamentId).Status, Is.Not.EqualTo(TournamentStatus.Completed),
                "title waits for the third-place play-off");

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = thirdPlace.Id!.Value, TournamentId = tournamentId, HomeScore = 1, AwayScore = 2,
            });

            var tournament = harness.Tournament(tournamentId);
            Assert.That(tournament.Status, Is.EqualTo(TournamentStatus.Completed), "both played -> concluded");

            var champion = harness.Participants(tournamentId)
                .Single(p => p.Id == harness.Match(final.Id!.Value).WinnerParticipantId);
            Assert.That(tournament.WinnerUserId, Is.EqualTo(champion.UserId),
                "the champion is the final's winner, never the play-off's");
        }
    }
}
