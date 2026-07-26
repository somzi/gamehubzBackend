using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;

namespace GameHubz.Logic.Test.Bracket
{
    /// <summary>
    /// The organiser-controlled draw: hand-placed bracket slots (including which positions stay empty
    /// as byes), hand-picked group sheets, and the pot draw. Random stays the default and is covered
    /// by the per-format generation fixtures.
    /// </summary>
    [TestFixture]
    internal sealed class BracketDrawTests
    {
        private static BracketDraw Manual(BracketDrawPlanDto plan) => new(BracketSeedingMode.Manual, plan);

        private static List<Guid> IdsInSeedOrder(BracketTestHarness harness, Guid tournamentId)
            => harness.Participants(tournamentId)
                .OrderBy(p => p.Seed ?? int.MaxValue)
                .Select(p => p.Id!.Value)
                .ToList();

        /// <summary>First-round slot layout as ids: index 2i / 2i+1 are the two sides of match i.</summary>
        private static List<Guid?> FirstRoundSlots(List<MatchEntity> matches)
            => matches
                .Where(m => m.RoundNumber == 1)
                .OrderBy(m => m.MatchOrder)
                .SelectMany(m => new[] { m.HomeParticipantId, m.AwayParticipantId })
                .ToList();

        private static List<Guid?> FirstRoundTeamSlots(List<TeamMatchEntity> matches)
            => matches
                .Where(m => m.RoundNumber == 1)
                .OrderBy(m => m.MatchOrder)
                .SelectMany(m => new[] { m.HomeTeamParticipantId, m.AwayTeamParticipantId })
                .ToList();

        // ---- Manual elimination draw ---------------------------------------

        [Test]
        public async Task ManualSlots_PlacesEveryEntrantExactlyWhereAsked()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 8);

            var ids = IdsInSeedOrder(harness, tournamentId);
            // A deliberately non-standard arrangement, so a generator that quietly re-seeded would fail.
            var plan = new BracketDrawPlanDto
            {
                Slots = new List<Guid?> { ids[3], ids[6], ids[0], ids[5], ids[7], ids[1], ids[2], ids[4] }
            };

            await harness.Service.GenerateSingleEliminationBracket(tournamentId, Manual(plan));

            Assert.That(FirstRoundSlots(harness.Matches(tournamentId)), Is.EqualTo(plan.Slots));
        }

        [Test]
        public async Task ManualSlots_EmptySlotsBecomeTheChosenByes()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 5);

            var ids = IdsInSeedOrder(harness, tournamentId);
            // Bracket of 8 for 5 entrants: the organiser hands the byes to ids[0], ids[2] and ids[4]
            // by leaving the slot opposite each of them empty.
            var plan = new BracketDrawPlanDto
            {
                Slots = new List<Guid?> { ids[0], null, ids[1], ids[3], ids[2], null, ids[4], null }
            };

            await harness.Service.GenerateSingleEliminationBracket(tournamentId, Manual(plan));

            var matches = harness.Matches(tournamentId);
            Assert.That(FirstRoundSlots(matches), Is.EqualTo(plan.Slots), "slot layout is taken verbatim");

            var byes = matches.Where(m => m.RoundNumber == 1 && m.Status == MatchStatus.Completed).ToList();
            Assert.That(byes.Count, Is.EqualTo(3), "one bye per empty slot");
            Assert.That(
                byes.Select(m => m.WinnerParticipantId).OrderBy(x => x),
                Is.EqualTo(new[] { ids[0], ids[2], ids[4] }.OrderBy(x => x)),
                "the entrants the organiser put opposite an empty slot are the ones who walk through");
        }

        [Test]
        public async Task ManualSlots_SeedsFollowThePositionTheEntrantWasPlacedIn()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);

            var ids = IdsInSeedOrder(harness, tournamentId);
            // Standard 4-slot spread is seeds 1, 4, 2, 3 — so whoever is placed first is seed 1.
            var plan = new BracketDrawPlanDto { Slots = new List<Guid?> { ids[2], ids[3], ids[1], ids[0] } };

            await harness.Service.GenerateSingleEliminationBracket(tournamentId, Manual(plan));

            var seedById = harness.Participants(tournamentId).ToDictionary(p => p.Id!.Value, p => p.Seed);
            Assert.That(seedById[ids[2]], Is.EqualTo(1));
            Assert.That(seedById[ids[3]], Is.EqualTo(4));
            Assert.That(seedById[ids[1]], Is.EqualTo(2));
            Assert.That(seedById[ids[0]], Is.EqualTo(3));
        }

        [Test]
        public async Task ManualSlots_MatchWithBothSidesEmpty_Throws()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 5);

            var ids = IdsInSeedOrder(harness, tournamentId);
            // All 5 placed, but the last match has nobody in it. A bye advances its lone entrant;
            // an empty match closes with no winner at all, so the round it feeds would wait forever.
            var plan = new BracketDrawPlanDto
            {
                Slots = new List<Guid?> { ids[0], ids[1], ids[2], ids[3], ids[4], null, null, null }
            };

            Assert.That(async () => await harness.Service.GenerateSingleEliminationBracket(tournamentId, Manual(plan)),
                Throws.Exception);
        }

        [Test]
        public async Task ManualSlots_ByesInSeparateMatches_IsAccepted()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 5);

            var ids = IdsInSeedOrder(harness, tournamentId);
            // Same 5 entrants and 3 byes, but every match keeps at least one side — the arrangement
            // an organiser is steered towards.
            var plan = new BracketDrawPlanDto
            {
                Slots = new List<Guid?> { ids[0], null, ids[1], ids[2], ids[3], null, ids[4], null }
            };

            Assert.That(async () => await harness.Service.GenerateSingleEliminationBracket(tournamentId, Manual(plan)),
                Throws.Nothing);

            var round2 = harness.Matches(tournamentId).Where(m => m.RoundNumber == 2).ToList();
            Assert.That(round2.Count, Is.EqualTo(2));
            Assert.That(
                round2.All(m => m.HomeParticipantId.HasValue || m.AwayParticipantId.HasValue),
                Is.True,
                "every second-round match already has at least one side from a bye or is waiting on a real fixture");
        }

        [Test]
        public async Task ManualSlots_WrongSlotCount_Throws()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 8);

            var ids = IdsInSeedOrder(harness, tournamentId);
            var plan = new BracketDrawPlanDto { Slots = ids.Take(6).Select(id => (Guid?)id).ToList() };

            Assert.That(async () => await harness.Service.GenerateSingleEliminationBracket(tournamentId, Manual(plan)),
                Throws.Exception);
        }

        [Test]
        public async Task ManualSlots_SameEntrantTwice_Throws()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);

            var ids = IdsInSeedOrder(harness, tournamentId);
            var plan = new BracketDrawPlanDto { Slots = new List<Guid?> { ids[0], ids[1], ids[2], ids[0] } };

            Assert.That(async () => await harness.Service.GenerateSingleEliminationBracket(tournamentId, Manual(plan)),
                Throws.Exception);
        }

        [Test]
        public async Task ManualSlots_EntrantLeftOut_Throws()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);

            var ids = IdsInSeedOrder(harness, tournamentId);
            // Right number of slots, but one entrant is dropped and its place left empty.
            var plan = new BracketDrawPlanDto { Slots = new List<Guid?> { ids[0], ids[1], ids[2], null } };

            Assert.That(async () => await harness.Service.GenerateSingleEliminationBracket(tournamentId, Manual(plan)),
                Throws.Exception);
        }

        [Test]
        public async Task ManualSlots_UnknownEntrant_Throws()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);

            var ids = IdsInSeedOrder(harness, tournamentId);
            var plan = new BracketDrawPlanDto { Slots = new List<Guid?> { ids[0], ids[1], ids[2], Guid.NewGuid() } };

            Assert.That(async () => await harness.Service.GenerateSingleEliminationBracket(tournamentId, Manual(plan)),
                Throws.Exception);
        }

        [Test]
        public async Task ManualSlots_AppliesToDoubleEliminationToo()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.DoubleElimination, 8);

            var ids = IdsInSeedOrder(harness, tournamentId);
            var plan = new BracketDrawPlanDto
            {
                Slots = new List<Guid?> { ids[7], ids[0], ids[6], ids[1], ids[5], ids[2], ids[4], ids[3] }
            };

            await harness.Service.GenerateDoubleEliminationBracket(tournamentId, Manual(plan));

            var winnersStageId = harness.Stages(tournamentId)
                .Single(s => s.Type == StageType.DoubleEliminationWinnersBracket).Id;
            var winnersBracket = harness.Matches(tournamentId)
                .Where(m => m.TournamentStageId == winnersStageId)
                .ToList();

            Assert.That(FirstRoundSlots(winnersBracket), Is.EqualTo(plan.Slots));
        }

        [Test]
        public async Task ManualSlots_AppliesToTeamBracketsToo()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedTeamTournamentAsync(TournamentFormat.SingleElimination, teamCount: 4, teamSize: 2);

            var ids = IdsInSeedOrder(harness, tournamentId);
            var plan = new BracketDrawPlanDto { Slots = new List<Guid?> { ids[1], ids[2], ids[3], ids[0] } };

            await harness.Service.GenerateTeamSingleEliminationBracket(tournamentId, Manual(plan));

            Assert.That(FirstRoundTeamSlots(harness.TeamMatches(tournamentId)), Is.EqualTo(plan.Slots));
        }

        [Test]
        public async Task ManualSlots_AppliesToTeamDoubleEliminationToo()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedTeamTournamentAsync(
                TournamentFormat.DoubleElimination, teamCount: 4, teamSize: 2);

            var ids = IdsInSeedOrder(harness, tournamentId);
            var plan = new BracketDrawPlanDto { Slots = new List<Guid?> { ids[3], ids[0], ids[2], ids[1] } };

            await harness.Service.GenerateTeamDoubleEliminationBracket(tournamentId, Manual(plan));

            var winnersStageId = harness.Stages(tournamentId)
                .Single(s => s.Type == StageType.DoubleEliminationWinnersBracket).Id;
            var winnersBracket = harness.TeamMatches(tournamentId)
                .Where(tm => tm.TournamentStageId == winnersStageId)
                .ToList();

            Assert.That(FirstRoundTeamSlots(winnersBracket), Is.EqualTo(plan.Slots));
        }

        // ---- Seeded elimination draw ---------------------------------------
        // Not offered in the picker yet (SupportedSeedingModes withholds it until players have a
        // rating — see that test), but the engine implements it and stays covered here so turning
        // it back on is a one-line change rather than a rewrite.

        [Test]
        public async Task SeededDraw_SpreadsRegistrationOrderOverTheStandardBracket()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 8);

            var ids = IdsInSeedOrder(harness, tournamentId);

            await harness.Service.GenerateSingleEliminationBracket(
                tournamentId, new BracketDraw(BracketSeedingMode.Seeded));

            var slots = FirstRoundSlots(harness.Matches(tournamentId));

            // Standard spread for 8: 1v8, 4v5, 2v7, 3v6 — seed 1 and seed 2 can only meet in the final.
            Assert.That(slots, Is.EqualTo(new List<Guid?>
            {
                ids[0], ids[7], ids[3], ids[4], ids[1], ids[6], ids[2], ids[5],
            }));

            var seedById = harness.Participants(tournamentId).ToDictionary(p => p.Id!.Value, p => p.Seed);
            Assert.That(seedById[ids[0]], Is.EqualTo(1), "first to register is seed 1");
        }

        [Test]
        public async Task SeededDraw_HandsTheByesToTheTopSeeds()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 5);

            var ids = IdsInSeedOrder(harness, tournamentId);

            await harness.Service.GenerateSingleEliminationBracket(
                tournamentId, new BracketDraw(BracketSeedingMode.Seeded));

            var byeWinners = harness.Matches(tournamentId)
                .Where(m => m.RoundNumber == 1 && m.Status == MatchStatus.Completed)
                .Select(m => m.WinnerParticipantId)
                .ToList();

            // Bracket of 8, 3 byes: seeds 1, 2 and 3 walk into round 2.
            Assert.That(byeWinners.OrderBy(x => x), Is.EqualTo(new[] { ids[0], ids[1], ids[2] }.OrderBy(x => x)));
        }

        // ---- Manual group draw ---------------------------------------------

        [Test]
        public async Task ManualGroups_UsesTheOrganisersGroupSheets()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.GroupStageWithKnockout, 8);

            var ids = IdsInSeedOrder(harness, tournamentId);
            var plan = new BracketDrawPlanDto
            {
                Groups = new List<List<Guid>>
                {
                    new() { ids[0], ids[2], ids[4], ids[6] },
                    new() { ids[1], ids[3], ids[5], ids[7] },
                }
            };

            await harness.Service.GenerateGroupStageWithKnockout(
                tournamentId, numberOfGroups: 2, qualifiersPerGroup: 2, draw: Manual(plan));

            var groups = harness.Groups(tournamentId).OrderBy(g => g.Name).ToList();
            var participants = harness.Participants(tournamentId);

            var groupA = participants.Where(p => p.TournamentGroupId == groups[0].Id).Select(p => p.Id!.Value).ToHashSet();
            var groupB = participants.Where(p => p.TournamentGroupId == groups[1].Id).Select(p => p.Id!.Value).ToHashSet();

            Assert.That(groupA, Is.EquivalentTo(plan.Groups[0]));
            Assert.That(groupB, Is.EquivalentTo(plan.Groups[1]));
        }

        [Test]
        public async Task ManualGroups_UnbalancedIsAllowed_AsLongAsEveryGroupCanPlay()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.GroupStageWithKnockout, 7);

            var ids = IdsInSeedOrder(harness, tournamentId);
            var plan = new BracketDrawPlanDto
            {
                Groups = new List<List<Guid>>
                {
                    new() { ids[0], ids[1], ids[2], ids[3], ids[4] },
                    new() { ids[5], ids[6] },
                }
            };

            await harness.Service.GenerateGroupStageWithKnockout(
                tournamentId, numberOfGroups: 2, qualifiersPerGroup: 2, draw: Manual(plan));

            // C(5,2) + C(2,2) = 10 + 1
            Assert.That(harness.Matches(tournamentId).Count, Is.EqualTo(11));
        }

        [Test]
        public async Task ManualGroups_GroupWithASingleEntrant_Throws()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.GroupStageWithKnockout, 5);

            var ids = IdsInSeedOrder(harness, tournamentId);
            // A one-entrant group generates no fixtures at all, so the stage could never finish.
            var plan = new BracketDrawPlanDto
            {
                Groups = new List<List<Guid>>
                {
                    new() { ids[0], ids[1], ids[2], ids[3] },
                    new() { ids[4] },
                }
            };

            Assert.That(async () => await harness.Service.GenerateGroupStageWithKnockout(
                    tournamentId, numberOfGroups: 2, qualifiersPerGroup: 2, draw: Manual(plan)),
                Throws.Exception);
        }

        [Test]
        public async Task ManualGroups_WrongGroupCount_Throws()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.GroupStageWithKnockout, 8);

            var ids = IdsInSeedOrder(harness, tournamentId);
            var plan = new BracketDrawPlanDto { Groups = new List<List<Guid>> { ids.ToList() } };

            Assert.That(async () => await harness.Service.GenerateGroupStageWithKnockout(
                    tournamentId, numberOfGroups: 2, qualifiersPerGroup: 2, draw: Manual(plan)),
                Throws.Exception);
        }

        [Test]
        public async Task ManualGroups_AppliesToTeamGroupStageToo()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedTeamTournamentAsync(
                TournamentFormat.GroupStageWithKnockout, teamCount: 8, teamSize: 2);

            var ids = IdsInSeedOrder(harness, tournamentId);
            var plan = new BracketDrawPlanDto
            {
                Groups = new List<List<Guid>>
                {
                    new() { ids[7], ids[6], ids[5], ids[4] },
                    new() { ids[3], ids[2], ids[1], ids[0] },
                }
            };

            await harness.Service.GenerateTeamGroupStageWithKnockout(
                tournamentId, numberOfGroups: 2, qualifiersPerGroup: 2, draw: Manual(plan));

            var groups = harness.Groups(tournamentId).OrderBy(g => g.Name).ToList();
            var participants = harness.Participants(tournamentId);

            Assert.That(
                participants.Where(p => p.TournamentGroupId == groups[0].Id).Select(p => p.Id!.Value),
                Is.EquivalentTo(plan.Groups[0]));
        }

        // ---- Pot draw -------------------------------------------------------

        [Test]
        public async Task PotDraw_GivesEveryGroupOneEntrantFromEachPot()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.GroupStageWithKnockout, 12);

            var ids = IdsInSeedOrder(harness, tournamentId);
            var pots = new List<List<Guid>>
            {
                ids.Take(4).ToList(),
                ids.Skip(4).Take(4).ToList(),
                ids.Skip(8).Take(4).ToList(),
            };

            await harness.Service.GenerateGroupStageWithKnockout(
                tournamentId, numberOfGroups: 4, qualifiersPerGroup: 2,
                draw: new BracketDraw(BracketSeedingMode.Pots, new BracketDrawPlanDto { Pots = pots }));

            var groups = harness.Groups(tournamentId);
            var participants = harness.Participants(tournamentId);

            Assert.That(groups.Count, Is.EqualTo(4));
            foreach (var group in groups)
            {
                var members = participants.Where(p => p.TournamentGroupId == group.Id).Select(p => p.Id!.Value).ToList();

                Assert.That(members.Count, Is.EqualTo(3), "one entrant per pot");
                foreach (var pot in pots)
                    Assert.That(members.Count(m => pot.Contains(m)), Is.EqualTo(1),
                        "each group draws exactly one name out of every pot");
            }
        }

        [Test]
        public async Task PotDraw_ShortFinalPotLeavesGroupsAtMostOneApart()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.GroupStageWithKnockout, 10);

            var ids = IdsInSeedOrder(harness, tournamentId);
            // 10 entrants over 4 groups = pots of 4, 4 and 2.
            var pots = new List<List<Guid>>
            {
                ids.Take(4).ToList(),
                ids.Skip(4).Take(4).ToList(),
                ids.Skip(8).Take(2).ToList(),
            };

            await harness.Service.GenerateGroupStageWithKnockout(
                tournamentId, numberOfGroups: 4, qualifiersPerGroup: 2,
                draw: new BracketDraw(BracketSeedingMode.Pots, new BracketDrawPlanDto { Pots = pots }));

            var participants = harness.Participants(tournamentId);
            var sizes = harness.Groups(tournamentId)
                .Select(g => participants.Count(p => p.TournamentGroupId == g.Id))
                .OrderBy(x => x)
                .ToList();

            Assert.That(sizes, Is.EqualTo(new[] { 2, 2, 3, 3 }));
        }

        [Test]
        public async Task PotDraw_AppliesToTeamGroupStageToo()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedTeamTournamentAsync(
                TournamentFormat.GroupStageWithKnockout, teamCount: 8, teamSize: 2);

            var ids = IdsInSeedOrder(harness, tournamentId);
            // 8 teams over 2 groups = 4 pots of 2.
            var pots = new List<List<Guid>>
            {
                ids.Take(2).ToList(),
                ids.Skip(2).Take(2).ToList(),
                ids.Skip(4).Take(2).ToList(),
                ids.Skip(6).Take(2).ToList(),
            };

            await harness.Service.GenerateTeamGroupStageWithKnockout(
                tournamentId, numberOfGroups: 2, qualifiersPerGroup: 2,
                draw: new BracketDraw(BracketSeedingMode.Pots, new BracketDrawPlanDto { Pots = pots }));

            var groups = harness.Groups(tournamentId);
            var participants = harness.Participants(tournamentId);

            foreach (var group in groups)
            {
                var members = participants.Where(p => p.TournamentGroupId == group.Id).Select(p => p.Id!.Value).ToList();

                Assert.That(members.Count, Is.EqualTo(4), "one team per pot");
                foreach (var pot in pots)
                    Assert.That(members.Count(m => pot.Contains(m)), Is.EqualTo(1),
                        "each group draws exactly one team out of every pot");
            }
        }

        [Test]
        public async Task PotDraw_PotOfTheWrongSize_Throws()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.GroupStageWithKnockout, 8);

            var ids = IdsInSeedOrder(harness, tournamentId);
            // 8 entrants over 2 groups needs 4 pots of 2 — 2 pots of 4 would put two pot-mates together.
            var pots = new List<List<Guid>>
            {
                ids.Take(4).ToList(),
                ids.Skip(4).Take(4).ToList(),
            };

            Assert.That(async () => await harness.Service.GenerateGroupStageWithKnockout(
                    tournamentId, numberOfGroups: 2, qualifiersPerGroup: 2,
                    draw: new BracketDraw(BracketSeedingMode.Pots, new BracketDrawPlanDto { Pots = pots })),
                Throws.Exception);
        }

        // ---- Draw options (what the picker is handed) ------------------------

        [Test]
        public async Task DrawOptions_ListTheEntrantsToPlace()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);

            var options = await harness.Service.GetDrawOptions(tournamentId);

            // The picker is useless without the names — it would report "everyone is placed" with
            // an empty bracket and refuse to generate.
            Assert.That(options.Entrants.Count, Is.EqualTo(4), "every entrant is handed to the picker");
            Assert.That(options.Entrants.Select(e => e.ParticipantId),
                Is.EquivalentTo(IdsInSeedOrder(harness, tournamentId)));
            Assert.That(options.Entrants.All(e => e.ParticipantId != Guid.Empty), Is.True);
            Assert.That(options.EntrantCount, Is.EqualTo(options.Entrants.Count),
                "the advertised count and the list must agree — the server validates against the list");
        }

        [Test]
        public async Task DrawOptions_DescribeTheEliminationShape()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 13);

            var options = await harness.Service.GetDrawOptions(tournamentId);

            Assert.That(options.Entrants.Count, Is.EqualTo(13));
            Assert.That(options.BracketSize, Is.EqualTo(16));
            Assert.That(options.ByeCount, Is.EqualTo(3));
            Assert.That(options.GroupsCount, Is.Null);
            Assert.That(options.SupportedModes, Does.Contain(BracketSeedingMode.Manual));
        }

        [Test]
        public async Task DrawOptions_DescribeTheGroupShape()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.GroupStageWithKnockout, 12, groupsCount: 4, qualifiersPerGroup: 2);

            var options = await harness.Service.GetDrawOptions(tournamentId);

            Assert.That(options.Entrants.Count, Is.EqualTo(12));
            Assert.That(options.GroupsCount, Is.EqualTo(4));
            Assert.That(options.QualifiersPerGroup, Is.EqualTo(2));
            Assert.That(options.PotCount, Is.EqualTo(3), "12 entrants over 4 groups = 3 pots");
            Assert.That(options.BracketSize, Is.Null);
            Assert.That(options.SupportedModes, Does.Contain(BracketSeedingMode.Pots));
        }

        [Test]
        public async Task DrawOptions_ForTeamTournament_UseTeamEntrants()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedTeamTournamentAsync(
                TournamentFormat.SingleElimination, teamCount: 4, teamSize: 2);

            var options = await harness.Service.GetDrawOptions(tournamentId);

            Assert.That(options.IsTeamTournament, Is.True);
            Assert.That(options.Entrants.Count, Is.EqualTo(4));
            Assert.That(options.Entrants.All(e => e.TeamId.HasValue), Is.True, "team entries carry their team id");
        }

        // ---- Mode availability ----------------------------------------------

        [Test]
        public void SupportedModes_MatchWhatEachFormatCanHonour()
        {
            // Seeded is withheld everywhere on purpose: with no player rating, "seed 1" would just
            // mean "registered first" — and that would decide the pairings, the byes and the group
            // split. The engine still implements it (see the SeededDraw_* tests), so this list is
            // the only thing to change when a ranking exists.
            Assert.That(BracketService.SupportedSeedingModes(TournamentFormat.SingleElimination),
                Is.EquivalentTo(new[] { BracketSeedingMode.Random, BracketSeedingMode.Manual }));
            Assert.That(BracketService.SupportedSeedingModes(TournamentFormat.DoubleElimination),
                Is.EquivalentTo(new[] { BracketSeedingMode.Random, BracketSeedingMode.Manual }));

            Assert.That(BracketService.SupportedSeedingModes(TournamentFormat.GroupStageWithKnockout),
                Is.EquivalentTo(new[] { BracketSeedingMode.Random, BracketSeedingMode.Manual, BracketSeedingMode.Pots }));

            Assert.That(
                BracketService.SupportedSeedingModes(TournamentFormat.GroupStageWithKnockout),
                Does.Not.Contain(BracketSeedingMode.Seeded),
                "no format offers a join-order seeding until players actually have a ranking");

            // A round-robin plays every pairing and Swiss round 1 comes off the standings order,
            // so neither has an opening arrangement worth hand-picking.
            Assert.That(BracketService.SupportedSeedingModes(TournamentFormat.League),
                Is.EquivalentTo(new[] { BracketSeedingMode.Random }));
            Assert.That(BracketService.SupportedSeedingModes(TournamentFormat.Swiss),
                Is.EquivalentTo(new[] { BracketSeedingMode.Random }));
        }
    }
}
