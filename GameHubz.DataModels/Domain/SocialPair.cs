namespace GameHubz.DataModels.Domain
{
    /// <summary>
    /// Helper used to normalize a pair of user ids for symmetric relations
    /// (friendships, direct chats, blocks). Storing pairs in a fixed order
    /// (smaller Guid first) keeps lookups simple and prevents accidental
    /// duplicate rows.
    /// </summary>
    public static class SocialPair
    {
        public static (Guid First, Guid Second) Normalize(Guid a, Guid b)
        {
            return a.CompareTo(b) <= 0 ? (a, b) : (b, a);
        }
    }
}
