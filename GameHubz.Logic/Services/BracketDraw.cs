using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// The organiser's seeding choice on its way from <see cref="BracketService.CreateBracket"/> into
    /// the generators. Deliberately carries the plan as raw participant ids rather than entities:
    /// every generator re-loads the tournament on its own (no-tracking) context, so resolving the
    /// ids against that generator's own participant list keeps one instance of each row in play.
    /// </summary>
    public sealed class BracketDraw
    {
        public BracketSeedingMode Mode { get; }

        /// <summary>The hand-made arrangement. Null for <see cref="BracketSeedingMode.Random"/> / Seeded.</summary>
        public BracketDrawPlanDto? Plan { get; }

        public BracketDraw(BracketSeedingMode mode, BracketDrawPlanDto? plan = null)
        {
            this.Mode = mode;
            this.Plan = plan;
        }

        /// <summary>The default draw — a plain shuffle, i.e. what every caller got before the picker existed.</summary>
        public static BracketDraw RandomDraw { get; } = new(BracketSeedingMode.Random);
    }
}
