namespace GameHubz.Logic.Interfaces
{
    public interface ITimeZoneProvider
    {
        TimeZoneInfo GetTimeZoneForCurrentContext();
    }
}
