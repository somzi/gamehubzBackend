using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    // A single stream attached to a match. We never store the video itself — only the link:
    // while Live we embed the streamer's channel, and on End we auto-resolve and store the VOD url.
    public class MatchStreamEntity : BaseEntity
    {
        public Guid MatchId { get; set; }
        public MatchEntity? Match { get; set; }

        public Guid StreamerUserId { get; set; }
        public UserEntity? Streamer { get; set; }

        // Streaming-capable SocialType only: Twitch, YouTube or Kick.
        public SocialType Platform { get; set; }

        // The channel handle/slug (or pasted url) the stream runs on. Denormalized so the link
        // stays valid even if the user later changes the channel on their profile.
        public string ChannelHandle { get; set; } = string.Empty;

        public MatchStreamStatus Status { get; set; }

        // Resolved after the stream ends. Null while live, or when auto-resolution failed
        // (e.g. Kick) and no manual fallback was supplied yet.
        public string? VodUrl { get; set; }

        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        // ─── Broadcast from the GameHubz app (Source = AppBroadcast) ───
        // Manual rows leave all of these at their defaults.
        public MatchStreamSource Source { get; set; }

        // SHA-256 (hex) of the session token the phone's broadcaster authenticates with. The phone's
        // screen-capture process has no GameHubz login of its own, so this token is its only credential.
        public string? BroadcastTokenHash { get; set; }

        public DateTime? LastHeartbeatOn { get; set; }

        // The server asked the phone to stop (match over and the streamer did not stop it). The next
        // heartbeat answers "stop".
        public DateTime? StopRequestedOn { get; set; }

        // "Your match is over — stop the stream" push, sent once.
        public DateTime? EndReminderSentOn { get; set; }
    }
}
