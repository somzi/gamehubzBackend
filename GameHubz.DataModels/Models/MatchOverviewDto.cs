using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class MatchOverviewDto
    {
        public Guid TournamentId { get; set; }

        public string TournamentName { get; set; } = string.Empty;

        public string HubName { get; set; } = string.Empty;

        public DateTime? ScheduledTime { get; set; }

        public DateTime? RoundDeadline { get; set; }

        public string OpponentName { get; set; } = string.Empty;
        public string? OpponentNickname { get; set; } = string.Empty;
        public string? UserNickname { get; set; } = string.Empty;

        public MatchStatus Status { get; set; }

        public Guid Id { get; set; }

        public Guid? HomeParticipantId { get; set; }
        public Guid? AwayParticipantId { get; set; }
        public string? OpponentAvatarUrl { get; set; }

        // Unread chat messages in this match for the requesting user (drives the
        // per-match chat badge in "My Matches").
        public int UnreadMessages { get; set; }

        // Series format, resolved against the tournament default, so the card can label how the
        // match is played before it is opened. 1 = a single game decides it.
        public int BestOf { get; set; } = 1;

        public TeamWinCondition SeriesWinCondition { get; set; }

        /// <summary>
        /// Which side of the fixture the requesting user plays. The list is built per user, so the
        /// card can label "you" without a second lookup — and knows whose ready-check stamp is whose.
        /// </summary>
        public bool IsHome { get; set; }

        /// <summary>
        /// Ready check, so the card can run its countdown (and offer the button) without the user
        /// opening the match. The two computed moments are null unless this match is actually
        /// running one: setting on, kick-off agreed, nothing ruled yet.
        /// </summary>
        public bool RequireMatchCheckIn { get; set; }
        public int? CheckInGraceMinutes { get; set; }
        public DateTime? HomeCheckedInOn { get; set; }
        public DateTime? AwayCheckedInOn { get; set; }
        public DateTime? CheckInOpensAt { get; set; }
        public DateTime? CheckInDeadline { get; set; }
    }
}