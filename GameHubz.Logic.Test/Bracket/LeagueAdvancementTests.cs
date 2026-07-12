using System;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;

namespace GameHubz.Logic.Test.Bracket
{
    // League result lifecycle beyond the single happy-path report: editing a result must resync
    // the standings from scratch (not double-count), deleting one must remove it, and playing the
    // whole fixture list must complete the tournament with the top of the table as winner.
    // SQLite harness (advancement path).
    [TestFixture]
    internal sealed class LeagueAdvancementTests
    {
        [Test]
        public async Task EditingAResult_ResyncsStandings_WithoutDoubleCounting()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tournamentId);

            var match = OpenLeagueMatch(harness, tournamentId);
            var homeId = match.HomeParticipantId!.Value;
            var awayId = match.AwayParticipantId!.Value;

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
            });

            // The typo fix: same match, flipped outcome. Standings must reflect ONLY the edited
            // result — the old 2:0 must leave no residue in points or goals.
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 1, AwayScore = 3,
            });

            var standings = await harness.NewService().GetLeagueStandings(tournamentId);
            var home = standings.Single(s => s.ParticipantId == homeId);
            var away = standings.Single(s => s.ParticipantId == awayId);

            Assert.That(home.Points, Is.EqualTo(0), "home's old win fully reverted");
            Assert.That(home.Wins, Is.EqualTo(0));
            Assert.That(home.Losses, Is.EqualTo(1));
            Assert.That(home.GoalsFor, Is.EqualTo(1), "only the edited score line counts");
            Assert.That(home.GoalsAgainst, Is.EqualTo(3));
            Assert.That(away.Points, Is.EqualTo(3), "away now holds the win");
            Assert.That(away.Wins, Is.EqualTo(1));
        }

        [Test]
        public async Task RevertingAResult_ReopensMatch_AndClearsItFromStandings()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tournamentId);

            var match = OpenLeagueMatch(harness, tournamentId);
            var homeId = match.HomeParticipantId!.Value;

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
            });

            await harness.NewService().RevertMatchResult(match.Id!.Value);

            var reverted = harness.Match(match.Id!.Value);
            Assert.That(reverted.Status, Is.Not.EqualTo(MatchStatus.Completed), "match reopened");
            Assert.That(reverted.HomeUserScore, Is.Null, "score line cleared");
            Assert.That(reverted.WinnerParticipantId, Is.Null);

            var standings = await harness.NewService().GetLeagueStandings(tournamentId);
            var home = standings.Single(s => s.ParticipantId == homeId);
            Assert.That(home.Points, Is.EqualTo(0), "deleted result contributes nothing");
            Assert.That(home.Wins, Is.EqualTo(0));
            Assert.That(home.GoalsFor, Is.EqualTo(0));
        }

        [Test]
        public async Task AllFixturesPlayed_CompletesLeague_WithTopOfTableAsWinner()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tournamentId);

            // Rounds unlock progressively, so keep picking the next open fixture.
            for (int guard = 0; guard < 50; guard++)
            {
                var playable = harness.Matches(tournamentId)
                    .FirstOrDefault(m => m.Status != MatchStatus.Completed
                        && m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue
                        && (m.RoundOpenAt == null || m.RoundOpenAt <= DateTime.UtcNow));
                if (playable == null) break;

                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = playable.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
                });
            }

            var tournament = harness.Tournament(tournamentId);
            Assert.That(tournament.Status, Is.EqualTo(TournamentStatus.Completed), "full fixture list completes the league");

            var standings = await harness.NewService().GetLeagueStandings(tournamentId);
            Assert.That(tournament.WinnerUserId, Is.EqualTo(standings.First().UserId),
                "the champion is the top of the final table");
        }

        // League rounds beyond the first are locked (future RoundOpenAt) and Matches() is unordered,
        // so always pick a fixture whose round is actually open.
        private static GameHubz.DataModels.Domain.MatchEntity OpenLeagueMatch(BracketTestHarness harness, Guid tournamentId)
            => harness.Matches(tournamentId)
                .First(m => m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue
                    && (m.RoundOpenAt == null || m.RoundOpenAt <= DateTime.UtcNow));
    }
}
