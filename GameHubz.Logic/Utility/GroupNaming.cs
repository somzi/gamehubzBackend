namespace GameHubz.Logic.Utility
{
    /// <summary>
    /// Group labels for the group stage. A field that fits the alphabet is lettered (A, B, ... Z);
    /// anything larger is numbered instead (1, 2, ... 30). One scheme per tournament — the old
    /// generator lettered the first 26 and then switched to raw ordinals mid-list ("Group 27"),
    /// which read as two naming schemes at once and, because digits sort before letters, put those
    /// groups first everywhere the list is ordered by name (knockout seeding, PDF export, tab strip).
    /// Keep in sync with src/lib/groups.ts in the mobile app.
    /// </summary>
    public static class GroupNaming
    {
        private const string Prefix = "Group ";

        /// <summary>Beyond this many groups the letters run out and the whole stage is numbered.</summary>
        public const int MaxLetteredGroups = 26;

        /// <summary>
        /// Label for the group at <paramref name="index"/> (0-based) in a stage of
        /// <paramref name="totalGroups"/> groups: "A" with 8 groups, "1" with 30.
        /// </summary>
        public static string Label(int index, int totalGroups)
        {
            if (index < 0) index = 0;

            // The index check is belt-and-braces: a caller passing a stale total must not walk
            // past 'Z' into the punctuation that follows it in ASCII.
            return totalGroups <= MaxLetteredGroups && index < MaxLetteredGroups
                ? ((char)('A' + index)).ToString()
                : (index + 1).ToString();
        }

        /// <summary>Full display name for the group at <paramref name="index"/>, e.g. "Group C".</summary>
        public static string Name(int index, int totalGroups) => Prefix + Label(index, totalGroups);

        /// <summary>
        /// Ordering key that puts the groups back in creation order. Plain alphabetical would give
        /// "Group 1", "Group 10", "Group 2"; shortest label first, then alphabetical, orders both
        /// schemes right — digit strings without leading zeros sort numerically once grouped by
        /// length. Legacy mixed "Group Z" / "Group 27" names sort correctly under the same rule.
        /// </summary>
        public static (int Length, string Label) SortKey(string? name)
        {
            string label = name ?? string.Empty;

            if (label.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                label = label.Substring(Prefix.Length);

            return (label.Length, label);
        }
    }
}
