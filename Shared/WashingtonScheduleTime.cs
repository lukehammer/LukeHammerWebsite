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

        public static DateTime PacificToday => Now.Date;

        public static DateTimeOffset ToEventInstant(DateTime date, TimeSpan? startTime)
        {
            var day = date.Date;
            if (!startTime.HasValue)
            {
                return AssumePacific(day);
            }

            var time = startTime.Value;
            if (time >= TimeSpan.FromDays(1))
            {
                return AssumePacific(day.AddDays(1));
            }

            return AssumePacific(day.Add(time));
        }

        public static bool IsEventInPast(DateTime date, TimeSpan? startTime)
        {
            var day = date.Date;
            if (day < PacificToday)
            {
                return true;
            }

            if (day > PacificToday)
            {
                return false;
            }

            if (!startTime.HasValue)
            {
                return false;
            }

            return ToEventInstant(date, startTime) < Now;
        }

        public const string PastEventMessage =
            "Events cannot be in the past. Choose today or a future date; if you set a start time, it must be later than now (Pacific time).";

        public static bool TryValidateEventNotInPast(DateTime date, TimeSpan? startTime, out string error)
        {
            if (IsEventInPast(date, startTime))
            {
                error = PastEventMessage;
                return false;
            }

            error = string.Empty;
            return true;
        }

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
