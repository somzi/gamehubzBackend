using System;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;

namespace GameHubz.Logic.Test.Bracket
{
    // Team-tournament result flow: sub-match results aggregate into the parent TeamMatch (win
    // condition), the winning team advances and the next fixture's sub-matches are created, and
    // an even split under MatchWins parks the fixture in TieBreakRequired. SQLite harness.
    [TestFixture]
    internal sealed class TeamAdvancementTests
    {
        [Test]
        public async Task AllSubMatchesDone_CompletesTeamMatch_AndAdvancesWinner()
        {
            const int teamSize = 2;
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedTeamTournamentAsync(TournamentFormat.SingleElimination, 4, teamSize);
            await harness.NewService().GenerateTeamSingleEliminationBracket(tournamentId);

            var round1 = harness.TeamMatches(tournamentId).Where(tm => tm.RoundNumber == 1).ToList();
            Assert.That(round1.Count, Is.EqualTo(2), "4 teams -> 2 first-round fixtures");
            var fixture = round1[0];

            var subMatches = harness.Matches(tournamentId).Where(m => m.TeamMatchId == fixture.Id).ToList();
            Assert.That(subMatches.Count, Is.EqualTo(teamSize), "one sub-match per roster slot");

            // First sub-match done: the fixture must NOT finalize early.
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = subMatches[0].Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
            });
            Assert.That(harness.TeamMatches(tournamentId).Single(tm => tm.Id == fixture.Id).Status,
                Is.Not.EqualTo(TeamMatchStatus.Completed), "waits for every sub-match");

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = subMatches[1].Id!.Value, TournamentId = tournamentId, HomeScore = 3, AwayScore = 1,
            });

            var completed = harness.TeamMatches(tournamentId).Single(tm => tm.Id == fixture.Id);
            Assert.That(completed.Status, Is.EqualTo(TeamMatchStatus.Completed), "all sub-matches in -> fixture finalized");
            Assert.That(completed.WinnerTeamParticipantId, Is.EqualTo(fixture.HomeTeamParticipantId),
                "home took both sub-matches");

            var final = harness.TeamMatches(tournamentId).Single(tm => tm.Id == fixture.NextTeamMatchId);
            bool advanced = final.HomeTeamParticipantId == completed.WinnerTeamParticipantId
                || final.AwayTeamParticipantId == completed.WinnerTeamParticipantId;
            Assert.That(advanced, Is.True, "winning team advanced into the final fixture");
        }

        [Test]
        public async Task BothFeedersDone_CreatesSubMatchesForTheNextFixture()
        {
            const int teamSize = 2;
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedTeamTournamentAsync(TournamentFormat.SingleElimination, 4, teamSize);
            await harness.NewService().GenerateTeamSingleEliminationBracket(tournamentId);

            var round1 = harness.TeamMatches(tournamentId).Where(tm => tm.RoundNumber == 1).ToList();
            var finalFixtureId = round1[0].NextTeamMatchId!.Value;

            Assert.That(harness.Matches(tournamentId).Count(m => m.TeamMatchId == finalFixtureId),
                Is.EqualTo(0), "precondition: the final fixture has no sub-matches yet");

            foreach (var fixture in round1)
            {
                var subMatches = harness.Matches(tournamentId).Where(m => m.TeamMatchId == fixture.Id).ToList();
                foreach (var sm in subMatches)
                {
                    await harness.NewService().UpdateMatchResult(new MatchResultDto
                    {
                        MatchId = sm.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
                    });
                }
            }

            var final = harness.TeamMatches(tournamentId).Single(tm => tm.Id == finalFixtureId);
            Assert.That(final.HomeTeamParticipantId, Is.Not.Null, "both winners seeded into the final");
            Assert.That(final.AwayTeamParticipantId, Is.Not.Null);

            var finalSubMatches = harness.Matches(tournamentId).Where(m => m.TeamMatchId == finalFixtureId).ToList();
            Assert.That(finalSubMatches.Count, Is.EqualTo(teamSize),
                "sub-matches materialize once both sides of the fixture are known");
        }

        [Test]
        public async Task EvenSplitUnderMatchWins_SetsTieBreakRequired()
        {
            const int teamSize = 2;
            var harness = new BracketTestHarness(useSqlite: true);
            // 2 teams -> the bracket is a single fixture; TeamWinCondition defaults to MatchWins.
            var tournamentId = await harness.SeedTeamTournamentAsync(TournamentFormat.SingleElimination, 2, teamSize);
            await harness.NewService().GenerateTeamSingleEliminationBracket(tournamentId);

            var fixture = harness.TeamMatches(tournamentId).Single();
            var subMatches = harness.Matches(tournamentId).Where(m => m.TeamMatchId == fixture.Id).ToList();

            // One sub-match each way: 1-1 on match wins with MatchWins scoring -> no winner.
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = subMatches[0].Id!.Value, TournamentId = tournamentId, HomeScore = 1, AwayScore = 0,
            });
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = subMatches[1].Id!.Value, TournamentId = tournamentId, HomeScore = 0, AwayScore = 1,
            });

            var tied = harness.TeamMatches(tournamentId).Single();
            Assert.That(tied.Status, Is.EqualTo(TeamMatchStatus.TieBreakRequired),
                "an even split can't be settled by match wins");
            Assert.That(tied.WinnerTeamParticipantId, Is.Null, "no winner until the tie-break");
            Assert.That(harness.Tournament(tournamentId).Status, Is.Not.EqualTo(TournamentStatus.Completed),
                "the title waits for the tie-break");
        }
    }
}
