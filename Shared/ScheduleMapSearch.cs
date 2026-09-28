using System;

namespace BlazorApp.Shared
{
    /// <summary>Google Maps search helpers for local sports venues (Vancouver, WA area).</summary>
    public static class ScheduleMapSearch
    {
        public const string AreaAnchor = "Vancouver, WA";

        public static string BuildSearchQuery(string? location)
        {
            if (location is not { } raw || string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var trimmed = raw.Trim();
            if (AlreadyAnchored(trimmed))
            {
                return trimmed;
            }

            return $"{trimmed}, {AreaAnchor}";
        }

        public static string? BuildGoogleMapsSearchUrl(string? location)
        {
            var query = BuildSearchQuery(location);
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            return "https://www.google.com/maps/search/?api=1&query="
                   + Uri.EscapeDataString(query);
        }

        private static bool AlreadyAnchored(string location)
        {
            if (location.IndexOf("Vancouver", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (location.IndexOf(", WA", StringComparison.OrdinalIgnoreCase) >= 0
                || location.EndsWith(" WA", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}
