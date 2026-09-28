using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorApp.Shared
{
    public static class ScheduleLocationSuggestions
    {
        /// <summary>
        /// Suggested locations: most frequently used plus the most recently updated events' locations (deduped).
        /// </summary>
        public static IReadOnlyList<string> Build(
            IEnumerable<Event>? events,
            int topByCount = 5,
            int recentCount = 5)
        {
            if (events == null)
            {
                return Array.Empty<string>();
            }

            var withLocation = events
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Location))
                .ToList();

            if (withLocation.Count == 0)
            {
                return Array.Empty<string>();
            }

            var byCount = withLocation
                .GroupBy(e => e.Location.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Take(topByCount)
                .Select(g => g.First().Location.Trim());

            var byRecent = withLocation
                .OrderByDescending(e => e.LastModifiedAt ?? DateTimeOffset.MinValue)
                .ThenByDescending(e => e.Date)
                .Select(e => e.Location.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(recentCount);

            return byCount
                .Concat(byRecent)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>All distinct locations for &lt;input list&gt; autocomplete, most-used first.</summary>
        public static IReadOnlyList<string> BuildAutocompleteList(IEnumerable<Event>? events)
        {
            if (events == null)
            {
                return Array.Empty<string>();
            }

            return events
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Location))
                .GroupBy(e => e.Location.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.First().Location.Trim())
                .ToList();
        }
    }
}
