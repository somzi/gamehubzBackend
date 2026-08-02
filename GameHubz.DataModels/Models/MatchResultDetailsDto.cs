using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class MatchResultDetailDto
    {
        // Lets a client that opened the match from a bare deep link (no bracket context)
        // know whether the match is already Completed instead of guessing from the scores.
        public MatchStatus? Status { get; set; }

        // Display name: Nickname when set, Username otherwise. Kept as the single collapsed
        // name the older app builds still render on their own.
        public string HomeUser { get; set; } = string.Empty;
        public string AwayUser { get; set; } = string.Empty;

        // The two identities kept apart so the client can show both (account name + in-game
        // nickname) instead of one ambiguous label. Nickname is null when the user never set
        // one — it is persisted as "" (entity default), which we normalize away here.
        public string HomeUsername { get; set; } = string.Empty;
        public string AwayUsername { get; set; } = string.Empty;
        public string? HomeNickname { get; set; }
        public string? AwayNickname { get; set; }

        public Guid? HomeUserId { get; set; }
        public Guid? AwayUserId { get; set; }
        public int HomeUserScore { get; set; }
        public int AwayUserScore { get; set; }
        public List<string> Evidences { get; set; } = [];
        public DateTime? ScheduledTime { get; set; }
        public string? AwayUserAvatarUrl { get; set; }
        public string? HomeUserAvatarUrl { get; set; }

        public bool RequireResultApproval { get; set; }
        public int? ProposedHomeScore { get; set; }
        public int? ProposedAwayScore { get; set; }
        public Guid? ProposedByUserId { get; set; }
        public Guid? HubOwnerUserId { get; set; }

        public bool AdminHelpRequested { get; set; }
        public Guid? AdminHelpRequestedByUserId { get; set; }
    }
}