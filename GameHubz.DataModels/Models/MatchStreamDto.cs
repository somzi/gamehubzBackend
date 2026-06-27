using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class MatchStreamDto
    {
        public Guid Id { get; set; }
        public Guid MatchId { get; set; }

        public Guid StreamerUserId { get; set; }
        public string? StreamerUsername { get; set; }
        public string? StreamerNickname { get; set; }
        public string? StreamerAvatarUrl { get; set; }

        public SocialType Platform { get; set; }
        public string ChannelHandle { get; set; } = string.Empty;

        // Convenience channel page url built from Platform + ChannelHandle (the app builds the
        // actual embed itself, since Twitch embeds need a parent domain tied to the WebView host).
        public string? ChannelUrl { get; set; }

        public MatchStreamStatus Status { get; set; }
        public string? VodUrl { get; set; }

        // Ended but no VOD link yet → the app shows the manual-entry fallback (mainly for Kick).
        public bool VodPending { get; set; }

        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
    }
}
