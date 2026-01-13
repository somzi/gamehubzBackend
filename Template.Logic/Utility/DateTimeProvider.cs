namespace Template.Logic.Utility
{
    public class DateTimeProvider
    {
        //private readonly ITimeZoneProvider timeZoneProvider;

        public DateTimeProvider()//ITimeZoneProvider timeZoneProvider)
        {
            //this.timeZoneProvider = timeZoneProvider;
        }

        public DateTime Now()
        {
            return DateTime.UtcNow;
        }
    }
}