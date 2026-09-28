using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace BlazorApp.Shared
{
    public class ScheduleGoingSelectionPrefs
    {
        [JsonPropertyName("includeMe")]
        public bool IncludeMe { get; set; } = true;

        [JsonPropertyName("selectedNames")]
        public List<string> SelectedNames { get; set; } = new();
    }

    public static class ScheduleGoingSelectionDefaults
    {
        /// <summary>First-time default for grandparents / family share flow.</summary>
        public static readonly IReadOnlyList<string> InitialSpectatorNames = new[]
        {
            "Stevie",
            "Grandpa"
        };

        public static ScheduleGoingSelectionPrefs CreateFirstTimePrefs()
        {
            return new ScheduleGoingSelectionPrefs
            {
                IncludeMe = true,
                SelectedNames = InitialSpectatorNames.ToList()
            };
        }

        public static ScheduleGoingSelectionPrefs Normalize(
            ScheduleGoingSelectionPrefs? prefs,
            IReadOnlyList<string> availableSpectatorNames)
        {
            prefs ??= CreateFirstTimePrefs();
            availableSpectatorNames ??= System.Array.Empty<string>();

            var selected = new List<string>();
            foreach (var raw in prefs.SelectedNames ?? new List<string>())
            {
                if (!ScheduleSpectators.TryNormalizeName(raw, out var normalized))
                {
                    continue;
                }

                if (!selected.Any(s => string.Equals(s, normalized, System.StringComparison.OrdinalIgnoreCase)))
                {
                    selected.Add(normalized);
                }
            }

            if (selected.Count == 0)
            {
                foreach (var name in InitialSpectatorNames)
                {
                    if (availableSpectatorNames.Any(s =>
                            string.Equals(s, name, System.StringComparison.OrdinalIgnoreCase)))
                    {
                        selected.Add(name);
                    }
                }
            }

            selected.Sort(System.StringComparer.Ordinal);

            return new ScheduleGoingSelectionPrefs
            {
                IncludeMe = prefs.IncludeMe,
                SelectedNames = selected
            };
        }
    }
}
