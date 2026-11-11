using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    /// <summary>Body of "invite these players": one entry per player.</summary>
    public class SendTournamentInvitesRequest
    {
        public List<TournamentInviteTarget> Invites { get; set; } = new();
    }

    public class TournamentInviteTarget
    {
        public Guid UserId { get; set; }

        /// <summary>
        /// The organizer knowingly invites a player outside the tournament's country / region /
        /// exclusive scope. Required for such a player — without it the invite is refused, so a
        /// list filter can never hand out an exception by accident.
        /// </summary>
        public bool AsGuest { get; set; }
    }

    /// <summary>What happened to each requested invite.</summary>
    public class TournamentInviteSendResultDto
    {
        public List<TournamentInviteSendItemDto> Items { get; set; } = new();
    }

    public class TournamentInviteSendItemDto
    {
        public Guid UserId { get; set; }

        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// "sent", "alreadyInvited", "alreadyIn", "declinedBefore", "banned", "needsGuest" or
        /// "notFound". Only "sent" created an invite and notified the player.
        /// </summary>
        public string Outcome { get; set; } = string.Empty;
    }

    /// <summary>A player the organizer may invite, as the invite picker shows them.</summary>
    public class TournamentInviteCandidateDto
    {
        public Guid UserId { get; set; }

        public string Username { get; set; } = string.Empty;

        public string? Nickname { get; set; }

        public string? AvatarUrl { get; set; }

        public string? Country { get; set; }

        /// <summary>
        /// Inside the tournament's country / region / exclusive scope. False = can only be invited
        /// as a guest.
        /// </summary>
        public bool MeetsRequirements { get; set; }

        /// <summary>"none", "pending" or "declined" — a declined player cannot be invited again.</summary>
        public string InviteState { get; set; } = "none";
    }

    /// <summary>One invite as the organizer's list shows it.</summary>
    public class TournamentInviteDto
    {
        public Guid Id { get; set; }

        public Guid TournamentId { get; set; }

        public Guid UserId { get; set; }

        public string Username { get; set; } = string.Empty;

        public string? AvatarUrl { get; set; }

        public TournamentInviteKind Kind { get; set; }

        /// <summary>Effective status: a pending entry invite of a tournament that stopped taking players reads Expired.</summary>
        public TournamentInviteStatus Status { get; set; }

        public bool IsGuest { get; set; }

        public Guid? SwapOutgoingUserId { get; set; }

        public string? SwapOutgoingUsername { get; set; }

        public DateTime CreatedOn { get; set; }

        public DateTime? RespondedOn { get; set; }
    }

    /// <summary>The signed-in player's invite to one tournament — what the details screen banner renders.</summary>
    public class MyTournamentInviteDto
    {
        public Guid Id { get; set; }

        public TournamentInviteKind Kind { get; set; }

        /// <summary>Effective status — see <see cref="TournamentInviteDto.Status"/>.</summary>
        public TournamentInviteStatus Status { get; set; }

        public bool IsGuest { get; set; }

        public string? InvitedByUsername { get; set; }

        public string? SwapOutgoingUsername { get; set; }

        /// <summary>Swap invites: matches the spot has already played. They stay on the outgoing player's profile.</summary>
        public int SwapPlayedMatches { get; set; }

        /// <summary>Entry invites: every place is taken right now. Accepting waits until one frees up.</summary>
        public bool IsTournamentFull { get; set; }

        /// <summary>Entry invites: the tournament has not opened registration yet (scheduled opening).</summary>
        public bool RegistrationNotOpenYet { get; set; }
    }
}
