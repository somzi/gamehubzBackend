namespace GameHubz.DataModels.Enums
{
    public enum MatchStreamSource
    {
        // The streamer went live in the Twitch / Kick / YouTube app and attached the channel by hand.
        Manual = 0,

        // The GameHubz app itself captures the phone screen and sends it to the streamer's linked
        // Twitch or Kick channel. The phone keeps the row alive with heartbeats.
        AppBroadcast = 1
    }
}
