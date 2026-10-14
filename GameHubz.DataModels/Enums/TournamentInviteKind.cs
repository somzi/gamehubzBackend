namespace GameHubz.DataModels.Enums
{
    public enum TournamentInviteKind
    {
        /// <summary>A place in the tournament: accepting makes the player a participant.</summary>
        Entry = 1,

        /// <summary>
        /// Another player's place (participant swap): accepting hands that spot over. Matches the
        /// outgoing player already played stay theirs on every profile.
        /// </summary>
        Swap = 2,
    }
}
