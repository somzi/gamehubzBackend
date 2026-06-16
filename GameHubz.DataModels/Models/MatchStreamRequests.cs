using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class StartMatchStreamRequest
    {
        public SocialType Platform { get; set; }

        // Optional. When omitted, the channel is taken from the user's saved socials for this platform.
        // When provided, it is persisted to the user's socials for next time.
        public string? Handle { get; set; }
    }

    public class EndMatchStreamRequest
    {
        // Optional manual override / fallback. When omitted, the VOD is auto-resolved from the platform API.
        public string? VodUrl { get; set; }

        // Optional. Targets a specific streamer's row (admin override). Defaults to the caller's own stream.
        public Guid? StreamerUserId { get; set; }
    }

    public class SetMatchStreamVodRequest
    {
        // Manual VOD link — used as the silent fallback when auto-resolution can't find one (Kick).
        public string VodUrl { get; set; } = string.Empty;

        // Optional. Targets a specific streamer's row (admin override). Defaults to the caller's own stream.
        public Guid? StreamerUserId { get; set; }
    }
}
