using System;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;

namespace GameHubz.Logic.Test.Bracket
{
    // Double-elimination result flow: the WB loser drops into the Losers Bracket, the Grand Final
    // completes the tournament when the WB champion wins, and an LB-champion win forces (and then
    // resolves through) the reset final. SQLite harness (advancement path).
    [TestFixture]
    internal sealed class DoubleElimAdvancementTests
    {
        [Test]
        public async Task WinnersRoundOneLoser_DropsIntoLosersBracket()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.DoubleElimination, 4);
            await harness.NewService().GenerateDoubleEliminationBracket(tournamentId);

            var wbSemi = harness.Matches(tournamentId)
                .First(m => m.IsUpperBracket && m.RoundNumber == 1 && m.NextMatchLoserBracketId.HasValue);

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = wbSemi.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
            });

            var loserId = wbSemi.AwayParticipantId!.Value;
            var lbMatch = harness.Match(wbSemi.NextMatchLoserBracketId!.Value);
            bool loserDropped = lbMatch.HomeParticipantId == loserId || lbMatch.AwayParticipantId == loserId;
            Assert.That(loserDropped, Is.True, "the WB loser lands in the linked Losers Bracket match");
            Assert.That(lbMatch.IsUpperBracket, Is.False, "the drop target is a Losers Bracket match");

            var wbFinal = harness.Match(wbSemi.NextMatchId!.Value);
            bool winnerAdvanced = wbFinal.HomeParticipantId == wbSemi.HomeParticipantId
                || wbFinal.AwayParticipantId == wbSemi.HomeParticipantId;
            Assert.That(winnerAdvanced, Is.True, "the WB winner advances along the winners edge");
        }

        [Test]
        public async Task FullPlaythrough_WbChampionWinsGrandFinal_CompletesWithoutReset()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.DoubleElimination, 4);
            await harness.NewService().GenerateDoubleEliminationBracket(tournamentId);

            // Home always wins. In the Grand Final home = WB champion (undefeated), so no reset
            // final is ever needed.
            await PlayOutAsync(harness, tournamentId, grandFinalHomeWins: true);

            var tournament = harness.Tournament(tournamentId);
            Assert.That(tournament.Status, Is.EqualTo(TournamentStatus.Completed), "title decided in the Grand Final");

            var grandFinal = harness.Matches(tournamentId).Single(m => m.Stage == MatchStage.GrandFinal);
            var champion = harness.Participants(tournamentId).Single(p => p.Id == grandFinal.WinnerParticipantId);
            Assert.That(tournament.WinnerUserId, Is.EqualTo(champion.UserId), "champion = Grand Final winner");

            Assert.That(harness.Matches(tournamentId).Any(m => m.Stage == MatchStage.GrandFinalReset),
                Is.False, "WB champion won -> no reset final");
        }

        [Test]
        public async Task GrandFinal_LbChampionWin_ForcesResetFinal_WhoseWinnerTakesTheTitle()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.DoubleElimination, 4);
            await harness.NewService().GenerateDoubleEliminationBracket(tournamentId);

            // Away wins the Grand Final = the LB champion evens the score at one loss each.
            await PlayOutAsync(harness, tournamentId, grandFinalHomeWins: false);

            var reset = harness.Matches(tournamentId).SingleOrDefault(m => m.Stage == MatchStage.GrandFinalReset);
            Assert.That(reset, Is.Not.Null, "LB-champion win forces the reset final");
            Assert.That(harness.Tournament(tournamentId).Status, Is.Not.EqualTo(TournamentStatus.Completed),
                "title is NOT decided until the reset is played");
            Assert.That(reset!.HomeParticipantId, Is.Not.Null, "both finalists are already known");
            Assert.That(reset.AwayParticipantId, Is.Not.Null);

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = reset.Id!.Value, TournamentId = tournamentId, HomeScore = 3, AwayScore = 2,
            });

            var tournament = harness.Tournament(tournamentId);
            Assert.That(tournament.Status, Is.EqualTo(TournamentStatus.Completed), "reset final decides the title");

            var champion = harness.Participants(tournamentId)
                .Single(p => p.Id == harness.Match(reset.Id!.Value).WinnerParticipantId);
            Assert.That(tournament.WinnerUserId, Is.EqualTo(champion.UserId), "champion = reset final winner");
        }

        // Plays every reportable match home-win, except the Grand Final whose side is chosen by
        // the caller (home = WB champion, away = LB champion). Stops when nothing is playable —
        // for an away-win GF that leaves the freshly created reset final for the test to handle.
        private static async Task PlayOutAsync(BracketTestHarness harness, Guid tournamentId, bool grandFinalHomeWins)
        {
            for (int guard = 0; guard < 100; guard++)
            {
                var playable = harness.Matches(tournamentId)
                    .Where(m => m.Status != MatchStatus.Completed
                        && m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue
                        && (m.RoundOpenAt == null || m.RoundOpenAt <= DateTime.UtcNow))
                    // Keep the Grand Final last so the WB/LB finals both feed it first.
                    .OrderBy(m => m.Stage == MatchStage.GrandFinal ? 1 : 0)
                    .FirstOrDefault();
                if (playable == null) break;

                if (playable.Stage == MatchStage.GrandFinal && !grandFinalHomeWins)
                {
                    await harness.NewService().UpdateMatchResult(new MatchResultDto
                    {
                        MatchId = playable.Id!.Value, TournamentId = tournamentId, HomeScore = 1, AwayScore = 2,
                    });
                    return; // the reset final now exists; the test drives it explicitly
                }

                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = playable.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
                });
            }
        }
    }
}
