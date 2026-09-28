using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorApp.Shared
{
    public static class ScheduleAttendanceShare
    {
        /// <summary>
        /// e.g. Luke, Stevie, and I are going to Cohen's football game at Castle Rock Saturday October 10th at 1:00 PM.
        /// </summary>
        public static string BuildMessage(Event scheduleEvent, IReadOnlyList<string> attendeeLabels)
        {
            if (scheduleEvent == null)
            {
                return string.Empty;
            }

            var lead = FormatAttendeeLeadPhrase(attendeeLabels);
            var game = FormatGameDescription(scheduleEvent);
            var location = FormatLocationPhrase(scheduleEvent);
            var when = ScheduleDateDisplay.FormatShareWhen(scheduleEvent.Date, scheduleEvent.StartTime);

            if (string.IsNullOrWhiteSpace(location))
            {
                return $"{lead} going to {game} {when}.";
            }

            return $"{lead} going to {game} at {location} {when}.";
        }

        /// <summary>Subject + verb, e.g. "Luke, Stevie, and I are", "I am", "Luke is".</summary>
        public static string FormatAttendeeLeadPhrase(IReadOnlyList<string> attendeeLabels)
        {
            var ordered = OrderAttendeeLabels(attendeeLabels);
            if (ordered.Count == 0)
            {
                return "We are";
            }

            if (ordered.Count == 1)
            {
                return string.Equals(ordered[0], "I", StringComparison.Ordinal)
                    ? "I am"
                    : $"{ordered[0]} is";
            }

            return $"{FormatOxfordList(ordered)} are";
        }

        public static IReadOnlyList<string> OrderAttendeeLabels(IReadOnlyList<string> attendeeLabels)
        {
            var labels = (attendeeLabels ?? Array.Empty<string>())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var includeMe = labels.RemoveAll(l => string.Equals(l, "I", StringComparison.OrdinalIgnoreCase)) > 0;
            var others = labels
                .OrderBy(l => l, StringComparer.Ordinal)
                .ToList();

            if (includeMe)
            {
                others.Add("I");
            }

            return others;
        }

        public static string FormatOxfordList(IReadOnlyList<string> items)
        {
            if (items == null || items.Count == 0)
            {
                return string.Empty;
            }

            if (items.Count == 1)
            {
                return items[0];
            }

            if (items.Count == 2)
            {
                return $"{items[0]} and {items[1]}";
            }

            return string.Join(", ", items.Take(items.Count - 1)) + ", and " + items[items.Count - 1];
        }

        public static string FormatGameDescription(Event scheduleEvent)
        {
            var kids = ScheduleKids.NormalizeKidsList(scheduleEvent.Kids ?? new List<string>());
            var possessive = FormatPossessiveKids(kids);
            var sport = FormatSportForShare(scheduleEvent.Sport);
            return $"{possessive} {sport} game";
        }

        public static string FormatPossessiveKids(IReadOnlyList<string> kids)
        {
            if (kids == null || kids.Count == 0)
            {
                return "the kids'";
            }

            if (kids.Count == 1)
            {
                return $"{kids[0]}'s";
            }

            var ordered = kids.OrderBy(k => k, StringComparer.Ordinal).ToList();
            return $"{string.Join(" and ", ordered)}'s";
        }

        public static string FormatSportForShare(string? sport)
        {
            var normalized = SportNames.NormalizeOrDefault(sport, SportNames.Football);
            return normalized.ToLowerInvariant();
        }

        public static string FormatLocationPhrase(Event scheduleEvent)
        {
            var location = scheduleEvent.Location?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(location) || location == "—")
            {
                return string.Empty;
            }

            if (location.StartsWith("TBD", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return location;
        }
    }
}
