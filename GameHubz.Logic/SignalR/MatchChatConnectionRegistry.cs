namespace GameHubz.Logic.SignalR
{
    /// <summary>
    /// Which user each match-chat connection belongs to. SignalR tracks group membership but cannot
    /// say who a member is, and a group sometimes has to lose one specific user: a bracket swap moves
    /// a player out of a fixture whose chat they may have open, and from then on they must stop
    /// receiving it.
    ///
    /// In-process on purpose. The hubs run on one instance with no backplane, so the groups this
    /// mirrors live in this process too. A single lock is enough: joins, leaves and disconnects are
    /// rare next to the messages themselves.
    /// </summary>
    public class MatchChatConnectionRegistry
    {
        private readonly object gate = new();
        private readonly Dictionary<Guid, Dictionary<string, Guid>> usersByMatch = new();
        private readonly Dictionary<string, HashSet<Guid>> matchesByConnection = new();

        public void Add(Guid matchId, string connectionId, Guid userId)
        {
            lock (gate)
            {
                if (!usersByMatch.TryGetValue(matchId, out var users))
                    usersByMatch[matchId] = users = new Dictionary<string, Guid>();
                users[connectionId] = userId;

                if (!matchesByConnection.TryGetValue(connectionId, out var matches))
                    matchesByConnection[connectionId] = matches = new HashSet<Guid>();
                matches.Add(matchId);
            }
        }

        public void Remove(Guid matchId, string connectionId)
        {
            lock (gate)
            {
                Forget(matchId, connectionId);
            }
        }

        public void RemoveConnection(string connectionId)
        {
            lock (gate)
            {
                if (!matchesByConnection.TryGetValue(connectionId, out var matches)) return;
                foreach (var matchId in matches.ToList())
                    Forget(matchId, connectionId);
            }
        }

        /// <summary>
        /// The connections these users hold in the match's group, forgotten as they are handed out —
        /// the caller is about to remove them from the group.
        /// </summary>
        public List<string> TakeConnections(Guid matchId, IReadOnlyCollection<Guid> userIds)
        {
            lock (gate)
            {
                if (!usersByMatch.TryGetValue(matchId, out var users)) return new List<string>();

                var taken = users.Where(u => userIds.Contains(u.Value)).Select(u => u.Key).ToList();
                foreach (var connectionId in taken)
                    Forget(matchId, connectionId);
                return taken;
            }
        }

        private void Forget(Guid matchId, string connectionId)
        {
            if (usersByMatch.TryGetValue(matchId, out var users))
            {
                users.Remove(connectionId);
                if (users.Count == 0) usersByMatch.Remove(matchId);
            }

            if (matchesByConnection.TryGetValue(connectionId, out var matches))
            {
                matches.Remove(matchId);
                if (matches.Count == 0) matchesByConnection.Remove(connectionId);
            }
        }
    }
}
