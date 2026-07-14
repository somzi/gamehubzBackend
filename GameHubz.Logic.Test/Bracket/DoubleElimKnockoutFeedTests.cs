using System;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;

namespace GameHubz.Logic.Test.Bracket
{
    // The organizer-opted double-elimination knockout phase behind a feeding stage: groups → DE
    // bracket (played through to a champion) and Swiss → DE bracket (drawn from the frozen
    // standings). Exercises CheckAndAdvanceGroupStage / GenerateSwissKnockoutMatches on their
    // Winners+Losers branch, which the single-elim knockout tests never touch. SQLite harness.
    [TestFixture]
    internal sealed class DoubleElimKnockoutFeedTests
    {
        [Test]
        public async Task GroupsFeedDoubleElimKnockout_AndPlayThroughToChampion()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.GroupStageWithKnockout, 8,
                qualifiersPerGroup: 2, groupsCount: 2,
                knockoutEliminationType: KnockoutEliminationType.Double);
            await harness.NewService().GenerateGroupStageWithKnockout(tournamentId, numberOfGroups: 2, qualifiersPerGroup: 2);

            var wbStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.DoubleEliminationWinnersBracket);
            var lbStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.DoubleEliminationLosersBracket);
            Assert.That(harness.Matches(tournamentId).Any(m => m.TournamentStageId == wbStage.Id),
                Is.False, "knockout is empty until the groups finish");

            await PlayOutAsync(harness, tournamentId);

            // 4 qualifiers -> WB: 2 semis + WB final + GF, LB: 2 matches.
            var wbMatches = harness.Matches(tournamentId).Where(m => m.TournamentStageId == wbStage.Id).ToList();
            var lbMatches = harness.Matches(tournamentId).Where(m => m.TournamentStageId == lbStage.Id).ToList();
            Assert.That(wbMatches, Is.Not.Empty, "groups completing drew the Winners Bracket");
            Assert.That(lbMatches, Is.Not.Empty, "and the Losers Bracket");

            var entrants = wbMatches.Where(m => m.RoundNumber == 1)
                .SelectMany(m => new[] { m.HomeParticipantId, m.AwayParticipantId })
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();
            Assert.That(entrants.Count, Is.EqualTo(4), "2 qualifiers per group x 2 groups");

            var tournament = harness.Tournament(tournamentId);
            Assert.That(tournament.Status, Is.EqualTo(TournamentStatus.Completed), "played through the DE knockout");
            Assert.That(tournament.WinnerUserId, Is.Not.Null.And.Not.EqualTo(Guid.Empty), "champion recorded");
        }

        [Test]
        public async Task SwissFeedsDoubleElimKnockout_WinnersAndLosersDrawn()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.Swiss, 8,
                swissRoundsCount: 1, swissKnockoutQualifiers: 4, swissDirectQualifiers: 4,
                knockoutEliminationType: KnockoutEliminationType.Double);
            await harness.NewService().GenerateSwissTournament(tournamentId);

            var swissRound = harness.Matches(tournamentId).Where(m => m.RoundNumber == 1
                && m.TournamentStageId == harness.Stages(tournamentId).Single(s => s.Type == StageType.Swiss).Id).ToList();
            Assert.That(swissRound.Count, Is.EqualTo(4));

            foreach (var m in swissRound)
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = m.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
                });
            }

            var wbStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.DoubleEliminationWinnersBracket);
            var lbStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.DoubleEliminationLosersBracket);

            var wbFirstRound = harness.Matches(tournamentId)
                .Where(m => m.TournamentStageId == wbStage.Id && m.RoundNumber == 1)
                .ToList();
            Assert.That(wbFirstRound.Count, Is.EqualTo(2), "4 qualifiers -> 2 WB semis");
            Assert.That(harness.Matches(tournamentId).Count(m => m.TournamentStageId == lbStage.Id),
                Is.GreaterThan(0), "Losers Bracket drawn alongside");
            Assert.That(harness.Matches(tournamentId).Any(m => m.Stage == MatchStage.GrandFinal),
                Is.True, "Grand Final present");

            var winners = swissRound.Select(m => m.HomeParticipantId!.Value).ToHashSet();
            var entrants = wbFirstRound
                .SelectMany(m => new[] { m.HomeParticipantId, m.AwayParticipantId })
                .Select(id => id!.Value)
                .ToHashSet();
            Assert.That(entrants, Is.EquivalentTo(winners), "the four Swiss winners entered the WB");
        }

        private static async Task PlayOutAsync(BracketTestHarness harness, Guid tournamentId)
        {
            for (int guard = 0; guard < 200; guard++)
            {
                var playable = harness.Matches(tournamentId)
                    .Where(m => m.Status != MatchStatus.Completed
                        && m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue
                        && (m.RoundOpenAt == null || m.RoundOpenAt <= DateTime.UtcNow))
                    // Grand Final last, so both brackets feed it before it's reported.
                    .OrderBy(m => m.Stage == MatchStage.GrandFinal ? 1 : 0)
                    .FirstOrDefault();
                if (playable == null) break;

                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = playable.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
                });
            }
        }
    }
}
