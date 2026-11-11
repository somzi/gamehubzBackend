using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Test.Bracket
{
    /// <summary>
    /// Where winners-bracket losers drop into the losers bracket. The order changes from round to
    /// round so that a player who beat you in the WB and loses the very next WB round lands in the
    /// other half of the LB — the two of you can meet again at the LB semifinal at the earliest.
    /// Reversing every round (the old order) sent them straight back to your path instead.
    /// Checked on the generated links, solo and team, the same way a played bracket follows them.
    /// </summary>
    [TestFixture]
    internal sealed class DoubleEliminationDropOrderTests
    {
        /// <summary>
        /// The drop order read off a live 64-player Battlefy bracket: for WB round r, the LB match
        /// (by order in round 2r−2) that each WB match's loser drops into, top to bottom.
        /// </summary>
        [Test]
        public async Task SixtyFourPlayers_DropExactlyLikeBattlefy()
        {
            var bracket = await SoloBracket(64);

            Assert.That(DropOrder(bracket, 2), Is.EqualTo(Enumerable.Range(0, 16).Reverse()), "WB R2: reversed");
            Assert.That(DropOrder(bracket, 3), Is.EqualTo(new[] { 3, 2, 1, 0, 7, 6, 5, 4 }), "WB R3: reversed within each half");
            Assert.That(DropOrder(bracket, 4), Is.EqualTo(new[] { 2, 3, 0, 1 }), "WB R4: halves swapped");
            Assert.That(DropOrder(bracket, 5), Is.EqualTo(new[] { 0, 1 }), "WB R5: natural");
        }

        /// <summary>
        /// The case that started this: you lose in WB R3, the player who beat you loses in WB R4.
        /// You used to meet again in LB R6; now not before the LB semifinal (R9 of 10).
        /// </summary>
        [Test]
        public async Task SixtyFourPlayers_BeatenInWbRound3ByAPlayerWhoLosesRound4_MeetAgainNoEarlierThanLbRound9()
        {
            var bracket = await SoloBracket(64);

            foreach (var match in bracket.Upper(3))
            {
                Assert.That(bracket.EarliestRematch(match), Is.EqualTo(9), $"WB R3 match {match.Order}");
            }
        }

        [TestCase(8)]
        [TestCase(16)]
        [TestCase(32)]
        [TestCase(64)]
        [TestCase(128)]
        public async Task Solo_BeatenByAPlayerWhoLosesTheNextWbRound_MeetAgainNoEarlierThanTheLbSemifinal(int players)
        {
            AssertNoEarlyRematch(await SoloBracket(players));
        }

        /// <summary>Byes collapse LB matches and re-route around them; the guarantee still holds.</summary>
        [TestCase(45)]
        [TestCase(23)]
        public async Task Solo_WithByes_SameGuarantee(int players)
        {
            AssertNoEarlyRematch(await SoloBracket(players));
        }

        [TestCase(8)]
        [TestCase(16)]
        [TestCase(32)]
        public async Task Team_BeatenByATeamThatLosesTheNextWbRound_MeetAgainNoEarlierThanTheLbSemifinal(int teams)
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedTeamTournamentAsync(TournamentFormat.DoubleElimination, teams, teamSize: 2);
            await harness.Service.GenerateTeamDoubleEliminationBracket(tournamentId);

            var nodes = harness.TeamMatches(tournamentId).Select(m => new Node(
                m.Id!.Value,
                m.RoundNumber ?? 0,
                m.MatchOrder ?? 0,
                Upper: m.IsUpperBracket && !m.IsGrandFinal,
                Lower: !m.IsUpperBracket,
                RealPairing: m.HomeTeamParticipantId != null && m.AwayTeamParticipantId != null,
                m.NextTeamMatchId,
                m.NextTeamMatchLoserBracketId));

            AssertNoEarlyRematch(new Bracket(nodes));
        }

        // ---- Helpers -------------------------------------------------------------------------

        private static void AssertNoEarlyRematch(Bracket bracket)
        {
            int lbSemifinal = bracket.LowerRounds - 1;

            // Every WB round whose winner still has a WB round to lose (the WB final's winner goes to the GF).
            for (int round = 1; round < bracket.UpperRounds; round++)
            {
                foreach (var match in bracket.Upper(round).Where(m => m.LoserNext != null && (round > 1 || m.RealPairing)))
                {
                    Assert.That(bracket.EarliestRematch(match), Is.GreaterThanOrEqualTo(lbSemifinal),
                        $"WB R{round} match {match.Order}: its winner, losing WB R{round + 1}, met its victim again too early");
                }
            }
        }

        private static async Task<Bracket> SoloBracket(int players)
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.DoubleElimination, players);
            await harness.Service.GenerateDoubleEliminationBracket(tournamentId);

            return new Bracket(harness.Matches(tournamentId).Select(m => new Node(
                m.Id!.Value,
                m.RoundNumber ?? 0,
                m.MatchOrder ?? 0,
                Upper: m.IsUpperBracket && m.Stage != MatchStage.GrandFinal,
                Lower: !m.IsUpperBracket,
                RealPairing: m.HomeParticipantId != null && m.AwayParticipantId != null,
                m.NextMatchId,
                m.NextMatchLoserBracketId)));
        }

        private static List<int> DropOrder(Bracket bracket, int wbRound) =>
            bracket.Upper(wbRound).Select(m => bracket[m.LoserNext!.Value].Order).ToList();

        private sealed record Node(Guid Id, int Round, int Order, bool Upper, bool Lower, bool RealPairing, Guid? Next, Guid? LoserNext);

        private sealed class Bracket
        {
            private readonly Dictionary<Guid, Node> byId;

            public Bracket(IEnumerable<Node> nodes)
            {
                byId = nodes.ToDictionary(n => n.Id);
            }

            public Node this[Guid id] => byId[id];

            public int UpperRounds => byId.Values.Where(n => n.Upper).Max(n => n.Round);

            public int LowerRounds => byId.Values.Where(n => n.Lower).Max(n => n.Round);

            public List<Node> Upper(int round) =>
                byId.Values.Where(n => n.Upper && n.Round == round).OrderBy(n => n.Order).ToList();

            /// <summary>
            /// This WB match's loser goes down; its winner wins on and loses the next WB round. The
            /// first LB round where both paths reach the same match is their earliest rematch.
            /// </summary>
            public int EarliestRematch(Node wbMatch)
            {
                var loserPath = LowerPath(wbMatch.LoserNext).ToHashSet();
                var winnersNextMatch = byId[wbMatch.Next!.Value];
                foreach (var id in LowerPath(winnersNextMatch.LoserNext))
                {
                    if (loserPath.Contains(id)) return byId[id].Round;
                }

                return int.MaxValue;
            }

            private IEnumerable<Guid> LowerPath(Guid? start)
            {
                for (var id = start; id != null && byId.TryGetValue(id.Value, out var node) && node.Lower; id = node.Next)
                {
                    yield return node.Id;
                }
            }
        }
    }
}
