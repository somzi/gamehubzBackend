using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    /// <summary>
    /// An organizer asking one player into a solo tournament — either a place of its own
    /// (<see cref="TournamentInviteKind.Entry"/>) or another participant's spot
    /// (<see cref="TournamentInviteKind.Swap"/>). Nobody is ever put into a tournament without
    /// answering one: the player becomes a participant only when they accept.
    ///
    /// Rows are never deleted: a declined invite is the history that stops the same organizer from
    /// asking the same player into the same tournament again. At most one pending invite per player
    /// per tournament (partial unique index, migration 102).
    /// </summary>
    public class TournamentInviteEntity : BaseEntity
    {
        public Guid TournamentId { get; set; }

        public TournamentEntity? Tournament { get; set; }

        /// <summary>The invited player.</summary>
        public Guid UserId { get; set; }

        public UserEntity? User { get; set; }

        /// <summary>The manager who sent it; shown to the player and told when they answer.</summary>
        public Guid? InvitedByUserId { get; set; }

        public TournamentInviteKind Kind { get; set; }

        public TournamentInviteStatus Status { get; set; }

        /// <summary>
        /// Sent on purpose to a player outside the tournament's country / region / exclusive scope.
        /// Only such an invite lets that player in; a hub ban still keeps them out.
        /// </summary>
        public bool IsGuest { get; set; }

        /// <summary>Swap invites only: the participant whose spot is on offer.</summary>
        public Guid? SwapOutgoingUserId { get; set; }

        public DateTime? RespondedOn { get; set; }
    }
}
