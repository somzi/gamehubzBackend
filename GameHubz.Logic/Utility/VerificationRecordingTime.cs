namespace GameHubz.Logic.Utility
{
    public static class VerificationRecordingTime
    {
        // No usable metadata means accepted with a noRecordingTime flag.
        public static bool? Overlaps(DateTime? recordedOn, int? durationMs, long? clockOffsetMs,
            int? timeZoneOffsetMinutes, DateTime biometricVerifiedOn)
        {
            if (!recordedOn.HasValue || durationMs is not > 0) return null;
            return Normalize(recordedOn, durationMs, clockOffsetMs, timeZoneOffsetMinutes, biometricVerifiedOn).HasValue;
        }

        public static DateTime? Normalize(DateTime? recordedOn, int? durationMs, long? clockOffsetMs,
            int? timeZoneOffsetMinutes, DateTime biometricVerifiedOn)
        {
            if (!recordedOn.HasValue || durationMs is not > 0) return null;
            long offset = clockOffsetMs is >= -900000 and <= 900000 ? clockOffsetMs.Value : 0;
            int zone = timeZoneOffsetMinutes is >= -840 and <= 840 ? timeZoneOffsetMinutes.Value : 0;
            // Ticks let even extreme untrusted timestamps be checked without DateTime overflow.
            long time = recordedOn.Value.Ticks + offset * TimeSpan.TicksPerMillisecond;
            long margin = ((long)durationMs.Value + 60000) * TimeSpan.TicksPerMillisecond;
            bool Fits(long candidate) => candidate >= new DateTime(2015, 1, 1).Ticks && candidate <= DateTime.MaxValue.Ticks
                && Math.Abs(biometricVerifiedOn.Ticks - candidate) <= margin;
            if (Fits(time)) return new DateTime(time, DateTimeKind.Utc);
            long localTime = time - (long)zone * TimeSpan.TicksPerMinute;
            return Fits(localTime) ? new DateTime(localTime, DateTimeKind.Utc) : null;
        }
    }
}
