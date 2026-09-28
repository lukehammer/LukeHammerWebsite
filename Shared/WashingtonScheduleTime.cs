using System;

namespace BlazorApp.Shared
{
    /// <summary>
    /// Schedule metadata for the Hammer site is stored and shown in US Pacific (Washington).
    /// </summary>
    public static class WashingtonScheduleTime
    {
        private static readonly TimeZoneInfo Pacific = ResolvePacificTimeZone();

        /// <summary>Current time in Pacific with offset (for JSON at rest, e.g. 2026-09-27T19:00:00-07:00).</summary>
        public static DateTimeOffset Now =>
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Pacific);

        public static string FormatLastModified(DateTimeOffset? pacificTime)
        {
            if (!pacificTime.HasValue)
            {
                return "—";
            }

            return pacificTime.Value.ToString("g");
        }

        /// <summary>Legacy JSON stored Pacific wall clock without an offset.</summary>
        public static DateTimeOffset AssumePacific(DateTime wallClock)
        {
            var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
            return new DateTimeOffset(unspecified, Pacific.GetUtcOffset(unspecified));
        }

        public static DateTimeOffset NormalizeStoredTime(DateTimeOffset value)
        {
            if (value.Offset != TimeSpan.Zero)
            {
                return value;
            }

            return AssumePacific(value.DateTime);
        }

        private static TimeZoneInfo ResolvePacificTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
            }
            catch (InvalidTimeZoneException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
            }
        }
    }
}
