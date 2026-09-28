using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BlazorApp.Shared
{
    public static class ScheduleEventPickers
    {
        public const int TimeStepMinutes = 30;
        public static readonly TimeSpan EarliestTime = TimeSpan.FromHours(6);
        public static readonly TimeSpan LatestTime = TimeSpan.FromHours(24);
        public static readonly TimeSpan TypicalStart = TimeSpan.FromHours(8);
        /// <summary>Last typical slot before late games (7:30 PM).</summary>
        public static readonly TimeSpan TypicalEnd = new TimeSpan(19, 30, 0);
        /// <summary>Late games start at 8:00 PM or later.</summary>
        public static readonly TimeSpan LateStart = TimeSpan.FromHours(20);

        public static DateTime MinSelectableDate() => WashingtonScheduleTime.PacificToday;

        public static DateTime MaxSelectableDate() => WashingtonScheduleTime.PacificToday.AddYears(2);

        public static bool IsSelectableDate(DateTime date)
        {
            var d = date.Date;
            return d >= WashingtonScheduleTime.PacificToday && d <= MaxSelectableDate();
        }

        public static DateTime NormalizeSelectableDate(DateTime date)
        {
            var d = date.Date;
            if (d < WashingtonScheduleTime.PacificToday)
            {
                return WashingtonScheduleTime.PacificToday;
            }

            var max = MaxSelectableDate();
            return d > max ? max : d;
        }

        public static IReadOnlyList<ScheduleDateOptionGroup> BuildDateOptionGroups(int daysAhead = 730)
        {
            var max = MaxSelectableDate();
            var lastDay = WashingtonScheduleTime.PacificToday.AddDays(daysAhead);
            if (lastDay > max)
            {
                lastDay = max;
            }

            var byMonth = new List<ScheduleDateOption>();
            for (var d = WashingtonScheduleTime.PacificToday; d <= lastDay; d = d.AddDays(1))
            {
                byMonth.Add(new ScheduleDateOption(
                    FormatDateForInput(d),
                    FormatDateOptionLabel(d)));
            }

            return byMonth
                .GroupBy(o => o.MonthKey, StringComparer.Ordinal)
                .Select(g => new ScheduleDateOptionGroup(
                    g.First().MonthLabel,
                    g.ToList()))
                .ToList();
        }

        /// <summary>Fixed-width label for date &lt;select&gt; options: MM/dd/yyyy — weekday.</summary>
        public static string FormatDateOptionLabel(DateTime date) => FormatDatePickerLabel(date);

        public static string FormatDatePickerLabel(DateTime date)
        {
            var numeric = date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
            var weekday = date.ToString("dddd", CultureInfo.CurrentCulture);
            return $"{numeric} — {weekday.PadRight(WeekdayColumnWidth)}";
        }

        private const int WeekdayColumnWidth = 9;

        public static string FormatDateForInput(DateTime date) =>
            date.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static bool TryParseDateFromInput(string? value, out DateTime date)
        {
            date = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (DateTime.TryParseExact(
                    value!.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                date = parsed.Date;
                return true;
            }

            return false;
        }

        public static string FormatDateHint(DateTime date) => FormatDatePickerLabel(date);

        public static string FormatTimeForStorage(TimeSpan time) =>
            $"{(int)time.TotalHours:D2}:{time.Minutes:D2}";

        public static bool TryParseTimeFromStorage(string? value, out TimeSpan time)
        {
            time = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (string.Equals(value!.Trim(), "24:00", StringComparison.OrdinalIgnoreCase))
            {
                time = TimeSpan.FromDays(1);
                return true;
            }

            return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out time);
        }

        public static string FormatTimeForSelect(TimeSpan? time)
        {
            if (!time.HasValue)
            {
                return string.Empty;
            }

            if (time.Value >= TimeSpan.FromDays(1))
            {
                return "24:00";
            }

            return FormatTimeForStorage(time.Value);
        }

        public static string FormatTimeLabel(TimeSpan time)
        {
            if (time >= TimeSpan.FromDays(1))
            {
                return "Midnight";
            }

            var clock = DateTime.Today.Add(time);
            return clock.ToString("h:mm tt", CultureInfo.CurrentCulture);
        }

        public static IReadOnlyList<ScheduleTimeSlotGroup> BuildTimeSlotGroups(
            TimeSpan? includeExisting = null,
            DateTime? eventDate = null)
        {
            var slots = new List<ScheduleTimeSlot>();
            for (var t = EarliestTime; t < LatestTime; t += TimeSpan.FromMinutes(TimeStepMinutes))
            {
                slots.Add(CreateSlot(t));
            }

            if (includeExisting is { } existing
                && existing >= EarliestTime
                && existing < LatestTime
                && (!eventDate.HasValue || !WashingtonScheduleTime.IsEventInPast(eventDate.Value.Date, existing)))
            {
                if (!slots.Any(s => s.Time == existing))
                {
                    slots.Add(CreateSlot(existing));
                    slots.Sort((a, b) => a.Time.CompareTo(b.Time));
                }
            }

            if (eventDate.HasValue)
            {
                slots = slots
                    .Where(s => !WashingtonScheduleTime.IsEventInPast(eventDate.Value.Date, s.Time))
                    .ToList();
            }

            var typical = slots.Where(s => s.Time >= TypicalStart && s.Time < LateStart).ToList();
            var early = slots.Where(s => s.Time >= EarliestTime && s.Time < TypicalStart).ToList();
            var late = slots.Where(s => s.Time >= LateStart).ToList();

            var groups = new List<ScheduleTimeSlotGroup>();
            if (typical.Count > 0)
            {
                groups.Add(new ScheduleTimeSlotGroup("Typical game times (8 AM – 7:30 PM)", typical));
            }

            if (early.Count > 0)
            {
                groups.Add(new ScheduleTimeSlotGroup("Early (6 – 7:30 AM)", early));
            }

            var midnight = new ScheduleTimeSlot(TimeSpan.FromDays(1), "24:00", "Midnight (12:00 AM)");
            if (!eventDate.HasValue || !WashingtonScheduleTime.IsEventInPast(eventDate.Value.Date, midnight.Time))
            {
                late.Add(midnight);
            }

            if (late.Count > 0)
            {
                groups.Add(new ScheduleTimeSlotGroup("Late (8 PM or later)", late));
            }

            return groups;
        }

        public static DateTime AddDaysFromToday(int days) =>
            WashingtonScheduleTime.PacificToday.AddDays(days);

        public static DateTime NextSaturdayFrom()
        {
            var date = WashingtonScheduleTime.PacificToday;
            var daysUntil = ((int)DayOfWeek.Saturday - (int)date.DayOfWeek + 7) % 7;
            return date.AddDays(daysUntil);
        }

        private static ScheduleTimeSlot CreateSlot(TimeSpan time) =>
            new(time, FormatTimeForStorage(time), FormatTimeLabel(time));
    }

    public sealed class ScheduleTimeSlot
    {
        public ScheduleTimeSlot(TimeSpan time, string value, string label)
        {
            Time = time;
            Value = value;
            Label = label;
        }

        public TimeSpan Time { get; }
        public string Value { get; }
        public string Label { get; }
    }

    public sealed class ScheduleTimeSlotGroup
    {
        public ScheduleTimeSlotGroup(string label, IReadOnlyList<ScheduleTimeSlot> slots)
        {
            Label = label;
            Slots = slots;
        }

        public string Label { get; }
        public IReadOnlyList<ScheduleTimeSlot> Slots { get; }
    }

    public sealed class ScheduleDateOption
    {
        public ScheduleDateOption(string value, string label)
        {
            Value = value;
            Label = label;
            if (DateTime.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                MonthKey = parsed.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                MonthLabel = parsed.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            }
            else
            {
                MonthKey = value;
                MonthLabel = value;
            }
        }

        public string Value { get; }
        public string Label { get; }
        public string MonthKey { get; }
        public string MonthLabel { get; }
    }

    public sealed class ScheduleDateOptionGroup
    {
        public ScheduleDateOptionGroup(string label, IReadOnlyList<ScheduleDateOption> options)
        {
            Label = label;
            Options = options;
        }

        public string Label { get; }
        public IReadOnlyList<ScheduleDateOption> Options { get; }
    }
}
