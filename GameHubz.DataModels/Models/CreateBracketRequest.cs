using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class CreateBracketRequest
    {
        public Guid TournamentId { get; set; }
        public int? GroupsCount { get; set; }
        public int? QualifiersPerGroup { get; set; }

        /// <summary>
        /// How the opening fixtures should be decided. Null = <see cref="BracketSeedingMode.Random"/>,
        /// which is what every client sent before the draw picker existed.
        /// </summary>
        public BracketSeedingMode? SeedingMode { get; set; }

        /// <summary>
        /// The organiser's arrangement. Required for <see cref="BracketSeedingMode.Manual"/> and
        /// <see cref="BracketSeedingMode.Pots"/>; ignored for Random / Seeded.
        /// </summary>
        public BracketDrawPlanDto? DrawPlan { get; set; }
    }

    /// <summary>
    /// A hand-made draw, expressed in TournamentParticipant ids (see the draw-options endpoint for
    /// the entrant list). Exactly one of the three shapes is used, picked by format + seeding mode.
    /// </summary>
    public class BracketDrawPlanDto
    {
        /// <summary>
        /// Elimination formats, Manual mode: one entry per bracket slot, in bracket order
        /// (slot 2i / 2i+1 are the two sides of first-round match i). Null entries are byes.
        /// Length must equal the bracket size (entrants rounded up to a power of two).
        /// </summary>
        public List<Guid?>? Slots { get; set; }

        /// <summary>
        /// Group formats, Manual mode: group index (0 = Group A) → the entrants in that group.
        /// Must cover every entrant exactly once, with at least 2 entrants per group.
        /// </summary>
        public List<List<Guid>>? Groups { get; set; }

        /// <summary>
        /// Group formats, Pots mode: pot index (0 = pot 1) → the entrants in that pot. Each pot holds
        /// one entrant per group (the last pot may be short), and the draw spreads every pot across
        /// the groups at random. Pot order carries no ranking — it is only a grouping the organiser
        /// wants kept apart.
        /// </summary>
        public List<List<Guid>>? Pots { get; set; }
    }
}
