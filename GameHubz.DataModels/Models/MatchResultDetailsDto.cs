using System.Text.Json.Serialization;
using GameHubz.DataModels.Catalog;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class MatchResultDetailDto
    {
        /// <summary>Effective Best-of for this match: its own override, or the tournament default.</summary>
        public int BestOf { get; set; } = 1;

        /// <summary>Effective tiebreak Best-of. Null = a level series replays the match's own format.</summary>
        public int? TiebreakBestOf { get; set; }

        /// <summary>Games won vs total score — how this match's series is settled.</summary>
        public TeamWinCondition SeriesWinCondition { get; set; }

        /// <summary>The games played so far, main series first, then any tiebreak series.</summary>
        public List<SeriesGame>? Games { get; set; }

        /// <summary>The games a pending proposal reported, when the tournament requires approval.</summary>
        public List<SeriesGame>? ProposedGames { get; set; }

        /// <summary>
        /// True when a level series here must be replayed rather than stand: solo knockout only.
        /// League / group / Swiss record a level series as a draw, and a level team sub-match is
        /// settled by the tie one level up. Lets the client offer "start tiebreak" only where the
        /// server would actually accept one.
        /// </summary>
        public bool AllowsTieBreak { get; set; }

        // The projection reads the stored JSON straight off the column — EF can't deserialize it
        // in-query — and the service hydrates Games/ProposedGames from these after materialization.
        // Never serialized to the client; the parsed lists above are the contract.
        [JsonIgnore]
        public string? GamesJson { get; set; }

        [JsonIgnore]
        public string? ProposedGamesJson { get; set; }

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

        /// <summary>Same evidence, typed. See MatchEvidenceItemDto for why both exist.</summary>
        public List<MatchEvidenceItemDto> EvidenceItems { get; set; } = [];
        public DateTime? ScheduledTime { get; set; }
        public string? AwayUserAvatarUrl { get; set; }
        public string? HomeUserAvatarUrl { get; set; }

        /// <summary>
        /// ISO 3166-1 alpha-2 country of each player, or null where they never set one. The point
        /// is ping: two players agreeing a match want to know whether they are a hop apart or a
        /// continent, and the flag says it faster than any label. Region is deliberately NOT sent
        /// alongside — it is derived from the country and nothing else (UserService.ApplyCountry),
        /// so a player with no country has no region either, just the GLOBAL default.
        /// </summary>
        public string? HomeUserCountry { get; set; }
        public string? AwayUserCountry { get; set; }

        /// <summary>Flag emoji for the codes above, resolved from the catalog. Null without one.</summary>
        public string? HomeUserCountryFlag => CountryCatalog.Get(HomeUserCountry)?.Flag;
        public string? AwayUserCountryFlag => CountryCatalog.Get(AwayUserCountry)?.Flag;

        /// <summary>Human country name for the codes above ("Serbia"), for a11y and tooltips.</summary>
        public string? HomeUserCountryName => CountryCatalog.Get(HomeUserCountry)?.Name;
        public string? AwayUserCountryName => CountryCatalog.Get(AwayUserCountry)?.Name;

        public bool RequireResultApproval { get; set; }

        /// <summary>
        /// Ready check state for this match. <see cref="RequireMatchCheckIn"/> is the tournament
        /// setting; the rest is null unless this match actually runs one (it needs a kick-off time).
        /// </summary>
        public bool RequireMatchCheckIn { get; set; }
        public int? CheckInGraceMinutes { get; set; }
        public DateTime? HomeCheckedInOn { get; set; }
        public DateTime? AwayCheckedInOn { get; set; }
        public DateTime? CheckInOpensAt { get; set; }
        public DateTime? CheckInDeadline { get; set; }
        public int? ProposedHomeScore { get; set; }
        public int? ProposedAwayScore { get; set; }
        public Guid? ProposedByUserId { get; set; }
        public Guid? HubOwnerUserId { get; set; }

        public bool AdminHelpRequested { get; set; }
        public Guid? AdminHelpRequestedByUserId { get; set; }
    }
}