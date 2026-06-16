namespace GameHubz.DataModels.Enums
{
    public enum MatchStreamStatus
    {
        // The streamer tapped "I'm streaming this match" — viewers see the live embed.
        Live = 1,

        // The streamer ended the stream. VodUrl is auto-resolved on end (or set manually as a fallback).
        Ended = 2
    }
}
