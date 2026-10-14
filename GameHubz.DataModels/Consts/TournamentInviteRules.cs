using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Consts
{
    /// <summary>
    /// When a tournament invite can still be answered. Evaluated on read instead of rewritten by every
    /// tournament transition (close, start, cancel, delete, complete …): the stored row stays Pending,
    /// and everything that shows or answers it asks this first.
    /// </summary>
    public static class TournamentInviteRules
    {
        /// <param name="tournamentStatus">Null when the tournament no longer exists (soft-deleted).</param>
        public static TournamentInviteStatus EffectiveStatus(
            TournamentInviteStatus stored,
            TournamentInviteKind kind,
            TournamentStatus? tournamentStatus)
        {
            if (stored != TournamentInviteStatus.Pending)
            {
                return stored;
            }

            return IsAnswerable(kind, tournamentStatus) ? TournamentInviteStatus.Pending : TournamentInviteStatus.Expired;
        }

        /// <summary>
        /// An entry invite lives while the tournament still takes players (a scheduled Draft included —
        /// it simply can't be accepted before registration opens). A full tournament doesn't expire it:
        /// accepting waits for a free place. A swap invite is meant for a running tournament and lives
        /// until the tournament is over; whether the swap is still allowed is checked when it is accepted.
        /// </summary>
        public static bool IsAnswerable(TournamentInviteKind kind, TournamentStatus? tournamentStatus)
        {
            if (tournamentStatus == null)
            {
                return false;
            }

            return kind == TournamentInviteKind.Swap
                ? tournamentStatus is not (TournamentStatus.Completed or TournamentStatus.Cancelled or TournamentStatus.Deleted)
                : tournamentStatus is TournamentStatus.Draft or TournamentStatus.RegistrationOpen;
        }
    }
}
