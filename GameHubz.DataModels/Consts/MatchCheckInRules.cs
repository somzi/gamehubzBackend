namespace GameHubz.DataModels.Consts
{
    /// <summary>
    /// The arithmetic of the ready check, in one place: the server enforces it, the sweep resolves
    /// by it, and the clients render countdowns from the same numbers. Every value is UTC.
    /// </summary>
    public static class MatchCheckInRules
    {
        /// <summary>Grace used when the organizer never picked one.</summary>
        public const int DefaultGraceMinutes = 10;

        /// <summary>
        /// Bounds an organizer can pick between. A zero-minute grace would forfeit the match in the
        /// same second the clock strikes, and anything past three hours stops being a ready check.
        /// </summary>
        public const int MinGraceMinutes = 1;
        public const int MaxGraceMinutes = 180;

        /// <summary>
        /// How long before kick-off the ready button lights up. Fixed rather than configurable:
        /// its only job is to let a punctual player confirm without waiting for the exact minute,
        /// and it never moves the forfeit deadline (see <see cref="Deadline"/>).
        /// </summary>
        public const int OpensBeforeMinutes = 15;

        public static int ResolveGraceMinutes(int? configured)
        {
            if (configured == null) return DefaultGraceMinutes;

            return Math.Clamp(configured.Value, MinGraceMinutes, MaxGraceMinutes);
        }

        public static DateTime OpensAt(DateTime scheduledStart)
            => scheduledStart.AddMinutes(-OpensBeforeMinutes);

        /// <summary>
        /// When the side that has not checked in loses the match. Counted from kick-off, or from the
        /// first check-in when that lands later — a player who is himself late must not be able to
        /// eat into the window his opponent was promised. With nobody checked in it is simply
        /// kick-off + grace, which is the moment the fixture is written off as a double no-show.
        /// </summary>
        public static DateTime Deadline(DateTime scheduledStart, DateTime? homeCheckedInOn, DateTime? awayCheckedInOn, int graceMinutes)
        {
            DateTime start = scheduledStart;

            if (homeCheckedInOn.HasValue && homeCheckedInOn.Value > start) start = homeCheckedInOn.Value;
            if (awayCheckedInOn.HasValue && awayCheckedInOn.Value > start) start = awayCheckedInOn.Value;

            // Deliberately unconditional: once both sides are in, no forfeit can follow, and callers
            // that render a countdown stop asking for this value rather than special-casing it here.
            return start.AddMinutes(graceMinutes);
        }
    }
}
