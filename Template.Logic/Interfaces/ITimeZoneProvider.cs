namespace Template.Logic.Interfaces
{
    public interface ITimeZoneProvider
    {
        TimeZoneInfo GetTimeZoneForCurrentContext();
    }
}