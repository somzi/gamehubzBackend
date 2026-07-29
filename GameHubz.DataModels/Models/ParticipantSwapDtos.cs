using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// Body of the swap call: the participant leaving the tournament and the hub member taking
    /// their place. The incoming user inherits the participant row, so every played match, group
    /// standing and bracket position transfers with it — see TournamentParticipantService.SwapParticipant.
    /// </summary>
    public class ParticipantSwapRequest
    {
        public Guid OutgoingUserId { get; set; }

        public Guid IncomingUserId { get; set; }
    }

    /// <summary>
    /// Whether a given participant may still be swapped out, plus the numbers the UI shows so the
    /// organizer understands *why*. The rule depends on the format:
    /// knockout / Swiss allow the swap only while the player has played nothing at all, while
    /// league and group formats allow it until the player has played a configured share of their fixtures.
    /// </summary>
    public class ParticipantSwapEligibilityDto
    {
        public Guid TournamentId { get; set; }

        public Guid UserId { get; set; }

        public string Username { get; set; } = string.Empty;

        public string? AvatarUrl { get; set; }

        public TournamentFormat Format { get; set; }

        public bool CanSwap { get; set; }

        /// <summary>Human-readable reason the swap is blocked; null when <see cref="CanSwap"/>.</summary>
        public string? BlockReason { get; set; }

        /// <summary>Matches with a recorded outcome (Completed or NoShow).</summary>
        public int PlayedMatches { get; set; }

        /// <summary>Every match this participant is currently slotted into, played or not.</summary>
        public int TotalMatches { get; set; }

        /// <summary>Rounded share for display only — the decision uses exact integer math.</summary>
        public int PlayedPercent { get; set; }

        /// <summary>
        /// The ceiling this format allows, or null for the zero-tolerance formats where a single
        /// played match already blocks the swap (<see cref="AllowsPartiallyPlayed"/> == false).
        /// </summary>
        public int? MaxPlayedPercent { get; set; }

        /// <summary>
        /// False for knockout and Swiss (one played match blocks), true for league / group formats
        /// where the swap survives until <see cref="MaxPlayedPercent"/> is reached.
        /// </summary>
        public bool AllowsPartiallyPlayed { get; set; }
    }

    /// <summary>
    /// A hub member who can be swapped in. The list already excludes current participants, so
    /// everything returned here is a valid target.
    /// </summary>
    public class ParticipantSwapCandidateDto
    {
        public Guid UserId { get; set; }

        public string Username { get; set; } = string.Empty;

        public string? AvatarUrl { get; set; }

        public HubRole HubRole { get; set; }
    }

    /// <summary>Result of a completed swap — what the client needs to re-render without a refetch.</summary>
    public class ParticipantSwapResultDto
    {
        public Guid TournamentId { get; set; }

        public Guid ParticipantId { get; set; }

        public Guid OutgoingUserId { get; set; }

        public string OutgoingUsername { get; set; } = string.Empty;

        public Guid IncomingUserId { get; set; }

        public string IncomingUsername { get; set; } = string.Empty;

        public string? IncomingAvatarUrl { get; set; }

        /// <summary>Played matches the incoming player inherited, for the confirmation copy.</summary>
        public int InheritedPlayedMatches { get; set; }
    }
}
