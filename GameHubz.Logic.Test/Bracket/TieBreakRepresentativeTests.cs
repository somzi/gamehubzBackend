using System;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Exceptions;

namespace GameHubz.Logic.Test.Bracket
{
    // The tie-break representative flow (TeamMatchService.SubmitRepresentative): an even fixture
    // parks in TieBreakRequired, each captain nominates one roster member, the second nomination
    // spawns the 1v1 tie-break sub-match, and its result finalizes the fixture through the normal
    // ProcessTeamMatchResult path. SQLite harness with real team + roster rows.
    [TestFixture]
    internal sealed class TieBreakRepresentativeTests
    {
        private const int TeamSize = 2;

        [Test]
        public async Task BothCaptainsNominate_TieBreakMatchDecidesTheFixture()
        {
            var (harness, tournamentId, fixtureId, home, away) = await SeedTiedFixtureAsync();

            // First nomination alone must not spawn the tie-break.
            var firstResponse = await harness.NewTeamMatchServiceAsUser(home.CaptainId)
                .SubmitRepresentative(fixtureId, new SubmitRepresentativeRequest { UserId = home.MemberId });
            Assert.That(firstResponse.TieBreakMatchId, Is.Null, "waits for the second captain");

            var secondResponse = await harness.NewTeamMatchServiceAsUser(away.CaptainId)
                .SubmitRepresentative(fixtureId, new SubmitRepresentativeRequest { UserId = away.CaptainId });
            Assert.That(secondResponse.TieBreakMatchId, Is.Not.Null, "both reps in -> tie-break spawned");

            var tieBreak = harness.Match(secondResponse.TieBreakMatchId!.Value);
            Assert.That(tieBreak.HomeUserId, Is.EqualTo(home.MemberId), "home rep plays the tie-break");
            Assert.That(tieBreak.AwayUserId, Is.EqualTo(away.CaptainId), "away rep plays the tie-break");
            Assert.That(harness.TeamMatches(tournamentId).Single(tm => tm.Id == fixtureId).Status,
                Is.EqualTo(TeamMatchStatus.Pending), "fixture re-armed for the tie-break result");

            // The tie-break result settles the fixture: 2-1 on match wins for home.
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = tieBreak.Id!.Value, TournamentId = tournamentId, HomeScore = 1, AwayScore = 0,
            });

            var decided = harness.TeamMatches(tournamentId).Single(tm => tm.Id == fixtureId);
            Assert.That(decided.Status, Is.EqualTo(TeamMatchStatus.Completed), "tie-break finalized the fixture");
            Assert.That(decided.WinnerTeamParticipantId, Is.EqualTo(decided.HomeTeamParticipantId),
                "home won the tie-break -> home takes the fixture");

            // 2-team bracket: the fixture was the final, so the tournament concludes.
            var tournament = harness.Tournament(tournamentId);
            Assert.That(tournament.Status, Is.EqualTo(TournamentStatus.Completed));
            Assert.That(tournament.WinnerTeamId, Is.EqualTo(home.TeamId), "champion team recorded");
        }

        [Test]
        public async Task OnlyACaptain_MayNominate_AndOnlyARosterMember()
        {
            var (harness, _, fixtureId, home, _) = await SeedTiedFixtureAsync();

            Assert.That(async () => await harness.NewTeamMatchServiceAsUser(Guid.NewGuid())
                    .SubmitRepresentative(fixtureId, new SubmitRepresentativeRequest { UserId = home.MemberId }),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("captain"),
                "an outsider cannot nominate");

            Assert.That(async () => await harness.NewTeamMatchServiceAsUser(home.CaptainId)
                    .SubmitRepresentative(fixtureId, new SubmitRepresentativeRequest { UserId = Guid.NewGuid() }),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("member"),
                "the representative must be on the roster");
        }

        [Test]
        public async Task Nomination_IsRejected_WhenNoTieBreakIsPending()
        {
            const int teamSize = 2;
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedTeamTournamentAsync(TournamentFormat.SingleElimination, 2, teamSize);

            var participants = harness.Participants(tournamentId);
            var captainId = Guid.NewGuid();
            await harness.SeedTeamRosterAsync(participants[0].TeamId!.Value, tournamentId, captainId, Guid.NewGuid());
            await harness.SeedTeamRosterAsync(participants[1].TeamId!.Value, tournamentId, Guid.NewGuid(), Guid.NewGuid());

            await harness.NewService().GenerateTeamSingleEliminationBracket(tournamentId);
            var fixture = harness.TeamMatches(tournamentId).Single();

            Assert.That(async () => await harness.NewTeamMatchServiceAsUser(captainId)
                    .SubmitRepresentative(fixture.Id!.Value, new SubmitRepresentativeRequest { UserId = captainId }),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("not required"),
                "an undecided fixture takes no representative");
        }

        private sealed record Roster(Guid TeamId, Guid CaptainId, Guid MemberId);

        // Seeds a 2-team bracket with real rosters, plays the two sub-matches to a 1-1 split
        // (MatchWins default) and returns the TieBreakRequired fixture with both rosters.
        private static async Task<(BracketTestHarness Harness, Guid TournamentId, Guid FixtureId, Roster Home, Roster Away)> SeedTiedFixtureAsync()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedTeamTournamentAsync(TournamentFormat.SingleElimination, 2, TeamSize);

            var participants = harness.Participants(tournamentId);
            var rosterA = new Roster(participants[0].TeamId!.Value, Guid.NewGuid(), Guid.NewGuid());
            var rosterB = new Roster(participants[1].TeamId!.Value, Guid.NewGuid(), Guid.NewGuid());
            await harness.SeedTeamRosterAsync(rosterA.TeamId, tournamentId, rosterA.CaptainId, rosterA.MemberId);
            await harness.SeedTeamRosterAsync(rosterB.TeamId, tournamentId, rosterB.CaptainId, rosterB.MemberId);

            await harness.NewService().GenerateTeamSingleEliminationBracket(tournamentId);

            var fixture = harness.TeamMatches(tournamentId).Single();
            var subMatches = harness.Matches(tournamentId).Where(m => m.TeamMatchId == fixture.Id).ToList();

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = subMatches[0].Id!.Value, TournamentId = tournamentId, HomeScore = 1, AwayScore = 0,
            });
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = subMatches[1].Id!.Value, TournamentId = tournamentId, HomeScore = 0, AwayScore = 1,
            });

            var tied = harness.TeamMatches(tournamentId).Single();
            Assert.That(tied.Status, Is.EqualTo(TeamMatchStatus.TieBreakRequired), "precondition: fixture is tied");

            // Home/away roster mapping follows the fixture's participant slots.
            var homeTeamId = harness.Participants(tournamentId).Single(p => p.Id == tied.HomeTeamParticipantId).TeamId!.Value;
            var (home, away) = homeTeamId == rosterA.TeamId ? (rosterA, rosterB) : (rosterB, rosterA);

            return (harness, tournamentId, fixture.Id!.Value, home, away);
        }
    }
}
