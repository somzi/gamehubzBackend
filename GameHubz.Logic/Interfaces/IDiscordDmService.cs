namespace GameHubz.Logic.Interfaces
{
    /// <summary>
    /// Dumb HTTP-only Discord bot DM transport: create-DM-channel + send-message over the REST API.
    /// Knows nothing about matches or tournaments — callers build the content and decide whether the
    /// recipient wants DMs (DiscordUserId linked + DiscordDmEnabled). Never throws: every failure is
    /// logged and swallowed (403 = the user blocked DMs or shares no server with the bot — expected).
    /// DMs are an additive channel; the Expo push always stays the primary one.
    /// </summary>
    public interface IDiscordDmService
    {
        /// <summary>Sends a DM and awaits the HTTP round-trip. Errors are swallowed internally.</summary>
        Task SendDmAsync(string discordUserId, string content);

        /// <summary>
        /// Fire-and-forget variant for request paths (mirrors the Task.Run push pattern). No-ops on a
        /// null/empty id, so call sites can pass an unlinked user's field directly. Safe inside
        /// Task.Run: the send never touches a request-scoped DbContext or cache.
        /// </summary>
        void SendDmInBackground(string? discordUserId, string content);
    }
}
