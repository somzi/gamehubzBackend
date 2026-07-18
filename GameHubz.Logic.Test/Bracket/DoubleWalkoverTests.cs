using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;

namespace GameHubz.Logic.Test.Bracket
{
    // Double walkover: an admin closes an unplayed elimination match with no winner (both sides
    // no-showed) and the opponent from the sibling matchup advances unopposed. Runs on the SQLite
    // harness because it shares the advancement/advisory-lock path with UpdateMatchResult.
    [TestFixture]
    internal sealed class DoubleWalkoverTests
    {
        [Test]
        public async Task DoubleWalkover_AfterSiblingDecided_AdvancesOpponentAndCompletes()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tid);

            var semis = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).ToList();
            Assert.That(semis.Count, Is.EqualTo(2));

            // Decide semi 0 normally; its winner sits in the final waiting for an opponent.
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = semis[0].Id!.Value, TournamentId = tid, HomeScore = 2, AwayScore = 1,
            });
            var advancingId = semis[0].HomeParticipantId!.Value;

            // Semi 1 was never played by either side → double walkover.
            await harness.NewService().ApplyDoubleWalkover(semis[1].Id!.Value);

            var voided = harness.Match(semis[1].Id!.Value);
            Assert.That(voided.Status, Is.EqualTo(MatchStatus.Completed), "voided match is closed");
            Assert.That(voided.WinnerParticipantId, Is.Null, "double walkover leaves no winner");

            var final = harness.Match(semis[0].NextMatchId!.Value);
            Assert.That(final.Status, Is.EqualTo(MatchStatus.Completed), "final settled by walkover");
            Assert.That(final.WinnerParticipantId, Is.EqualTo(advancingId), "surviving opponent wins by walkover");

            Assert.That(harness.Tournament(tid).Status, Is.EqualTo(TournamentStatus.Completed), "champion decided");
        }

        [Test]
        public async Task DoubleWalkover_BeforeSiblingDecided_AdvancesOnceSiblingCompletes()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tid);

            var semis = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).ToList();

            // Apply the double walkover while the sibling semi-final is still pending.
            await harness.NewService().ApplyDoubleWalkover(semis[0].Id!.Value);

            var finalBefore = harness.Match(semis[0].NextMatchId!.Value);
            Assert.That(finalBefore.Status, Is.Not.EqualTo(MatchStatus.Completed),
                "final still waits on the live sibling");

            // Now decide the sibling — its winner should walk over the final automatically.
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = semis[1].Id!.Value, TournamentId = tid, HomeScore = 3, AwayScore = 0,
            });
            var advancingId = semis[1].HomeParticipantId!.Value;

            var final = harness.Match(semis[0].NextMatchId!.Value);
            Assert.That(final.Status, Is.EqualTo(MatchStatus.Completed), "final settled once the sibling is decided");
            Assert.That(final.WinnerParticipantId, Is.EqualTo(advancingId), "the sibling's winner takes the walkover");
            Assert.That(harness.Tournament(tid).Status, Is.EqualTo(TournamentStatus.Completed));
        }

        [Test]
        public async Task DoubleWalkover_OnCompletedMatch_Throws()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tid);

            var semi = harness.Matches(tid).First(m => m.RoundNumber == 1);
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = semi.Id!.Value, TournamentId = tid, HomeScore = 2, AwayScore = 1,
            });

            Assert.That(async () => await harness.NewService().ApplyDoubleWalkover(semi.Id!.Value),
                Throws.Exception, "an already-completed match can't be double-walkover'd");
        }

        // League / Swiss / group double walkovers are covered in NoShowWalkoverTests — they now
        // close the fixture as a NoShow double forfeit instead of being rejected.

        // ── Team sub-matches ────────────────────────────────────────────────────────────────
        // A sub-match double walkover closes ONE game of a tie as NoShow (contributing nothing);
        // the tie itself resolves once every game is terminal: played games alone decide it, and
        // when NO game was played the whole tie is voided (elimination: both teams out, the
        // opponent walks over; league: nothing awarded).

        [Test]
        public async Task TeamSub_DoubleWalkover_ClosesGameOnly_TieStaysOpen()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedTeamTournamentAsync(TournamentFormat.SingleElimination, 4, teamSize: 2);
            await harness.NewService().GenerateTeamSingleEliminationBracket(tid);

            var fixture = harness.TeamMatches(tid).First(tm => tm.RoundNumber == 1);
            var subs = harness.Matches(tid).Where(m => m.TeamMatchId == fixture.Id).OrderBy(m => m.MatchOrder).ToList();
            Assert.That(subs.Count, Is.EqualTo(2));

            await harness.NewService().ApplyDoubleWalkover(subs[0].Id!.Value);

            Assert.That(harness.Match(subs[0].Id!.Value).Status, Is.EqualTo(MatchStatus.NoShow),
                "the voided game closes as a no-show");
            Assert.That(harness.TeamMatches(tid).Single(tm => tm.Id == fixture.Id).Status,
                Is.EqualTo(TeamMatchStatus.Pending), "the tie still waits on the other game");
        }

        [Test]
        public async Task TeamSub_OnePlayedOneWalkover_PlayedGameDecidesTheTie()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedTeamTournamentAsync(TournamentFormat.SingleElimination, 4, teamSize: 2);
            await harness.NewService().GenerateTeamSingleEliminationBracket(tid);

            var fixture = harness.TeamMatches(tid).First(tm => tm.RoundNumber == 1);
            var subs = harness.Matches(tid).Where(m => m.TeamMatchId == fixture.Id).OrderBy(m => m.MatchOrder).ToList();

            // Game 1: home player wins. Game 2: nobody showed → double walkover.
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = subs[0].Id!.Value, TournamentId = tid, HomeScore = 2, AwayScore = 0,
            });
            await harness.NewService().ApplyDoubleWalkover(subs[1].Id!.Value);

            var decided = harness.TeamMatches(tid).Single(tm => tm.Id == fixture.Id);
            Assert.That(decided.Status, Is.EqualTo(TeamMatchStatus.Completed), "all games terminal → tie resolves");
            Assert.That(decided.WinnerTeamParticipantId, Is.EqualTo(decided.HomeTeamParticipantId),
                "the only played game decides the tie");

            var final = harness.TeamMatches(tid).Single(tm => tm.Id == fixture.NextTeamMatchId!.Value);
            Assert.That(final.HomeTeamParticipantId == decided.WinnerTeamParticipantId
                || final.AwayTeamParticipantId == decided.WinnerTeamParticipantId,
                "the tie winner advanced into the next round");
        }

        [Test]
        public async Task TeamSub_DrawnGamePlusWalkover_StillGoesToTieBreak()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedTeamTournamentAsync(TournamentFormat.SingleElimination, 4, teamSize: 2);
            await harness.NewService().GenerateTeamSingleEliminationBracket(tid);

            var fixture = harness.TeamMatches(tid).First(tm => tm.RoundNumber == 1);
            var subs = harness.Matches(tid).Where(m => m.TeamMatchId == fixture.Id).OrderBy(m => m.MatchOrder).ToList();

            // Game 1 played to a draw, game 2 voided → the teams DID play and are even → tie-break,
            // not a void.
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = subs[0].Id!.Value, TournamentId = tid, HomeScore = 1, AwayScore = 1,
            });
            await harness.NewService().ApplyDoubleWalkover(subs[1].Id!.Value);

            Assert.That(harness.TeamMatches(tid).Single(tm => tm.Id == fixture.Id).Status,
                Is.EqualTo(TeamMatchStatus.TieBreakRequired), "an even, partially-played tie goes to a tie-break");
        }

        [Test]
        public async Task TeamSub_AllWalkovers_VoidsTie_SiblingWinnerWalksOver()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedTeamTournamentAsync(TournamentFormat.SingleElimination, 4, teamSize: 2);
            await harness.NewService().GenerateTeamSingleEliminationBracket(tid);

            var semis = harness.TeamMatches(tid).Where(tm => tm.RoundNumber == 1).OrderBy(tm => tm.MatchOrder).ToList();
            Assert.That(semis.Count, Is.EqualTo(2));

            // Semi 0 decided normally (home team sweeps both games).
            foreach (var sub in harness.Matches(tid).Where(m => m.TeamMatchId == semis[0].Id).ToList())
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = sub.Id!.Value, TournamentId = tid, HomeScore = 2, AwayScore = 0,
                });
            }
            var advancingTeamParticipantId = harness.TeamMatches(tid).Single(tm => tm.Id == semis[0].Id).WinnerTeamParticipantId;
            Assert.That(advancingTeamParticipantId, Is.Not.Null);

            // Semi 1: neither game was played → void the whole tie game by game.
            foreach (var sub in harness.Matches(tid).Where(m => m.TeamMatchId == semis[1].Id).ToList())
                await harness.NewService().ApplyDoubleWalkover(sub.Id!.Value);

            var voided = harness.TeamMatches(tid).Single(tm => tm.Id == semis[1].Id);
            Assert.That(voided.Status, Is.EqualTo(TeamMatchStatus.Completed), "the voided tie is closed");
            Assert.That(voided.WinnerTeamParticipantId, Is.Null, "a voided tie has no winner — both teams are out");

            var final = harness.TeamMatches(tid).Single(tm => tm.Id == semis[0].NextTeamMatchId!.Value);
            Assert.That(final.Status, Is.EqualTo(TeamMatchStatus.Completed), "final settled by walkover");
            Assert.That(final.WinnerTeamParticipantId, Is.EqualTo(advancingTeamParticipantId),
                "the sibling's winner takes the walkover");

            Assert.That(harness.Tournament(tid).Status, Is.EqualTo(TournamentStatus.Completed), "champion decided");
        }

        [Test]
        public async Task TeamSub_AllWalkovers_BeforeSiblingDecided_SettlesOnceSiblingCompletes()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedTeamTournamentAsync(TournamentFormat.SingleElimination, 4, teamSize: 2);
            await harness.NewService().GenerateTeamSingleEliminationBracket(tid);

            var semis = harness.TeamMatches(tid).Where(tm => tm.RoundNumber == 1).OrderBy(tm => tm.MatchOrder).ToList();

            // Void semi 0 first, while semi 1 is still live — nothing may settle yet.
            foreach (var sub in harness.Matches(tid).Where(m => m.TeamMatchId == semis[0].Id).ToList())
                await harness.NewService().ApplyDoubleWalkover(sub.Id!.Value);

            var finalBefore = harness.TeamMatches(tid).Single(tm => tm.Id == semis[0].NextTeamMatchId!.Value);
            Assert.That(finalBefore.Status, Is.Not.EqualTo(TeamMatchStatus.Completed),
                "final still waits on the live sibling");

            // Semi 1 decided → its winner walks over the final automatically.
            foreach (var sub in harness.Matches(tid).Where(m => m.TeamMatchId == semis[1].Id).ToList())
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = sub.Id!.Value, TournamentId = tid, HomeScore = 3, AwayScore = 1,
                });
            }
            var advancingTeamParticipantId = harness.TeamMatches(tid).Single(tm => tm.Id == semis[1].Id).WinnerTeamParticipantId;

            var final = harness.TeamMatches(tid).Single(tm => tm.Id == semis[0].NextTeamMatchId!.Value);
            Assert.That(final.Status, Is.EqualTo(TeamMatchStatus.Completed), "final settled once the sibling decided");
            Assert.That(final.WinnerTeamParticipantId, Is.EqualTo(advancingTeamParticipantId));
            Assert.That(harness.Tournament(tid).Status, Is.EqualTo(TournamentStatus.Completed));
        }

        [Test]
        public async Task TeamSub_AllWalkovers_League_AwardsNothingToEitherTeam()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedTeamTournamentAsync(TournamentFormat.League, 2, teamSize: 2);
            await harness.NewService().GenerateTeamLeagueTournament(tid);

            var fixture = harness.TeamMatches(tid).Single();
            foreach (var sub in harness.Matches(tid).Where(m => m.TeamMatchId == fixture.Id).ToList())
                await harness.NewService().ApplyDoubleWalkover(sub.Id!.Value);

            var voided = harness.TeamMatches(tid).Single(tm => tm.Id == fixture.Id);
            Assert.That(voided.Status, Is.EqualTo(TeamMatchStatus.Completed), "the fixture is administratively closed");
            Assert.That(voided.WinnerTeamParticipantId, Is.Null, "no winner");

            foreach (var p in harness.Participants(tid))
            {
                Assert.That(p.Points, Is.EqualTo(0), "a double no-show awards no points — deliberately not a draw");
                Assert.That(p.Draws, Is.EqualTo(0), "and no draw is recorded");
                Assert.That(p.Wins, Is.EqualTo(0));
                Assert.That(p.Losses, Is.EqualTo(0));
            }
        }

        [Test]
        public async Task TeamSub_RealResultOverNoShow_ReopensTheVoidedTie()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedTeamTournamentAsync(TournamentFormat.SingleElimination, 2, teamSize: 2);
            await harness.NewService().GenerateTeamSingleEliminationBracket(tid);

            var fixture = harness.TeamMatches(tid).Single();
            var subs = harness.Matches(tid).Where(m => m.TeamMatchId == fixture.Id).OrderBy(m => m.MatchOrder).ToList();

            // Void the whole tie (it is the final → the tournament stays in progress, no champion).
            foreach (var sub in subs)
                await harness.NewService().ApplyDoubleWalkover(sub.Id!.Value);

            Assert.That(harness.TeamMatches(tid).Single().WinnerTeamParticipantId, Is.Null);
            Assert.That(harness.Tournament(tid).Status, Is.Not.EqualTo(TournamentStatus.Completed),
                "a voided final declares no champion");

            // A late real result lands on one of the no-show games → the tie reopens, re-aggregates,
            // and now resolves from the played game.
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = subs[0].Id!.Value, TournamentId = tid, HomeScore = 2, AwayScore = 1,
            });

            var decided = harness.TeamMatches(tid).Single();
            Assert.That(decided.Status, Is.EqualTo(TeamMatchStatus.Completed));
            Assert.That(decided.WinnerTeamParticipantId, Is.EqualTo(decided.HomeTeamParticipantId),
                "the late result decides the reopened tie");
            Assert.That(harness.Tournament(tid).Status, Is.EqualTo(TournamentStatus.Completed), "champion decided now");
        }

        [Test]
        public async Task TeamSub_RevertNoShow_ReopensVoidedLeagueFixture_WithoutCorruptingPoints()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedTeamTournamentAsync(TournamentFormat.League, 2, teamSize: 2);
            await harness.NewService().GenerateTeamLeagueTournament(tid);

            var fixture = harness.TeamMatches(tid).Single();
            var subs = harness.Matches(tid).Where(m => m.TeamMatchId == fixture.Id).OrderBy(m => m.MatchOrder).ToList();
            foreach (var sub in subs)
                await harness.NewService().ApplyDoubleWalkover(sub.Id!.Value);

            // Delete-result on one no-show game: the fixture reopens and — since the void awarded
            // nothing — nothing may be subtracted either.
            await harness.NewService().RevertMatchResult(subs[0].Id!.Value);

            Assert.That(harness.Match(subs[0].Id!.Value).Status, Is.EqualTo(MatchStatus.Pending),
                "the reverted game is open again");
            Assert.That(harness.TeamMatches(tid).Single().Status, Is.EqualTo(TeamMatchStatus.Pending),
                "the fixture reopened");

            foreach (var p in harness.Participants(tid))
            {
                Assert.That(p.Points, Is.EqualTo(0), "no phantom subtraction — the void never awarded points");
                Assert.That(p.Draws, Is.EqualTo(0));
            }
        }

        [Test]
        public async Task DoubleWalkover_DoubleElimination_AdvancesAcrossWinnerAndLoserEdges()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.DoubleElimination, 4);
            await harness.NewService().GenerateDoubleEliminationBracket(tid);

            // Both Winners-Bracket round-1 matches feed the WB final; both also drop their losers to LB.
            var wbR1 = harness.Matches(tid)
                .Where(m => m.IsUpperBracket && m.RoundNumber == 1
                    && m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue)
                .OrderBy(m => m.MatchOrder)
                .ToList();
            Assert.That(wbR1.Count, Is.EqualTo(2));

            // Double-walkover one WB match, then play the sibling.
            await harness.NewService().ApplyDoubleWalkover(wbR1[0].Id!.Value);
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = wbR1[1].Id!.Value, TournamentId = tid, HomeScore = 2, AwayScore = 1,
            });
            var siblingWinner = wbR1[1].HomeParticipantId!.Value;

            var voided = harness.Match(wbR1[0].Id!.Value);
            Assert.That(voided.WinnerParticipantId, Is.Null, "voided WB match has no winner");

            // Winner edge: the sibling's winner walks over the (now opponent-less) WB final.
            var wbFinal = harness.Match(wbR1[1].NextMatchId!.Value);
            Assert.That(wbFinal.Status, Is.EqualTo(MatchStatus.Completed), "WB final settled by walkover");
            Assert.That(wbFinal.WinnerParticipantId, Is.EqualTo(siblingWinner));
        }
    }
}
