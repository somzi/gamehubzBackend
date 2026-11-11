using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    /// <summary>A linked Twitch / Kick channel as the owner sees it. Tokens never leave the server.</summary>
    public class StreamAccountDto
    {
        public SocialType Platform { get; set; }
        public string Login { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? ChannelUrl { get; set; }
        public bool NeedsRelink { get; set; }
        public DateTime LinkedOn { get; set; }
    }

    /// <summary>Which platforms this server can link right now (credentials configured).</summary>
    public class StreamAccountsDto
    {
        public List<StreamAccountDto> Accounts { get; set; } = new();
        public List<SocialType> AvailablePlatforms { get; set; } = new();
    }

    public class PrepareBroadcastRequest
    {
        public SocialType Platform { get; set; }
    }

    /// <summary>
    /// Everything the phone needs to start sending: where to, with which key, how to authenticate its
    /// heartbeats, and what to draw on the overlay. The stream key is handed out only here, only to the
    /// channel owner, and is not stored by the server.
    /// </summary>
    public class BroadcastSessionDto
    {
        public string SessionToken { get; set; } = string.Empty;
        public SocialType Platform { get; set; }
        public string ChannelHandle { get; set; } = string.Empty;
        public string IngestUrl { get; set; } = string.Empty;
        public string StreamKey { get; set; } = string.Empty;
        public int HeartbeatIntervalSeconds { get; set; }
        public BroadcastOverlayDto Overlay { get; set; } = new();
    }

    public class BroadcastOverlayDto
    {
        public string TournamentName { get; set; } = string.Empty;
        public string HomeName { get; set; } = string.Empty;
        public string AwayName { get; set; } = string.Empty;
        public int? RoundNumber { get; set; }
        /// <summary>The streamer plays on the home side.</summary>
        public bool StreamerIsHome { get; set; }
    }

    public class BroadcastStartedDto
    {
        public Guid StreamId { get; set; }
    }

    public class BroadcastHeartbeatDto
    {
        /// <summary>The phone must stop sending: the stream was ended here, or the match is long over.</summary>
        public bool Stop { get; set; }
    }
}
