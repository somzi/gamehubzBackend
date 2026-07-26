namespace GameHubz.DataModels.Enums
{
    /// <summary>
    /// How a tournament's opening fixtures were decided when the bracket was generated. Persisted on
    /// the tournament so the client can show "how this bracket was drawn" long after generation.
    /// Null on rows created before the draw picker existed — every one of those was a random draw.
    /// </summary>
    public enum BracketSeedingMode
    {
        /// <summary>Engine shuffles the entrants. The historical (and default) behaviour.</summary>
        Random = 1,

        /// <summary>
        /// The organiser placed every entrant by hand: exact bracket slots (and which slots stay
        /// empty as byes) for elimination formats, or exact group membership for group formats.
        /// </summary>
        Manual = 2,

        /// <summary>
        /// Standard seeding from the entrants' order (seed 1 = first registered): 1 v N, 2 v N-1 …
        /// for elimination brackets, snake distribution across groups for the group stage.
        /// </summary>
        Seeded = 3,

        /// <summary>
        /// Group formats only. The organiser sorts entrants into pots and the draw takes exactly one
        /// entrant from each pot into each group, at random. A pot is just a set the organiser wants
        /// kept apart — it carries no ranking, so pot 1 is not "the strongest".
        /// </summary>
        Pots = 4
    }
}
