using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    // One per platform (Twitch / YouTube / Kick). Resolution is one-shot and triggered explicitly
    // when the streamer ends the stream — no background polling.
    public interface IStreamPlatformClient
    {
        SocialType Platform { get; }

        // Returns the VOD/replay url for the just-ended stream, or null if it can't be resolved
        // (missing credentials, API error, no VOD yet). Null is a normal outcome — callers fall
        // back to manual entry. Implementations must not throw for "not found".
        Task<string?> TryResolveVodUrlAsync(
            string handle,
            DateTime startedAtUtc,
            DateTime endedAtUtc,
            CancellationToken cancellationToken = default);
    }
}
