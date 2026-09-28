using System;
using System.Globalization;

namespace BlazorApp.Shared
{
    /// <summary>
    /// Plain-language schedule date/time for table display (day + month words, ordinal date, 12-hour time).
    /// </summary>
    public static class ScheduleDateDisplay
    {
        private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("en-US");

        /// <summary>
        /// e.g. Sat · Sep · 27th · 3:00 PM — split on " · " for separate UI chips.
        /// </summary>
        public static string FormatScheduleWhen(DateTime date, TimeSpan? startTime)
        {
            var day = date.ToString("ddd", DisplayCulture);
            var month = date.ToString("MMM", DisplayCulture);
            var dayOrdinal = FormatOrdinalDay(date.Day);
            var time = FormatStartTime(startTime);
            return $"{day} · {month} · {dayOrdinal} · {time}";
        }

        public static string FormatOrdinalDay(int day)
        {
            if (day is < 1 or > 31)
            {
                return day.ToString(CultureInfo.InvariantCulture);
            }

            if (day is >= 11 and <= 13)
            {
                return $"{day}th";
            }

            return (day % 10) switch
            {
                1 => $"{day}st",
                2 => $"{day}nd",
                3 => $"{day}rd",
                _ => $"{day}th",
            };
        }

        public static string FormatStartTime(TimeSpan? startTime) =>
            startTime.HasValue
                ? DateTime.Today.Add(startTime.Value).ToString("h:mm tt", DisplayCulture)
                : "unscheduled";
    }
}
