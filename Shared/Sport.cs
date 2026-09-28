using System;
using System.Linq;

namespace BlazorApp.Shared
{
    /// <summary>Well-known sport labels; events may use any normalized custom name.</summary>
    public static class SportNames
    {
        public const string Baseball = "Baseball";
        public const string Basketball = "Basketball";
        public const string Football = "Football";
        public const string Soccer = "Soccer";

        public static readonly string[] Defaults = { Baseball, Basketball, Football, Soccer };

        public static bool TryNormalize(string? input, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var trimmed = input!.Trim();
            if (trimmed.Length > 40)
            {
                return false;
            }

            if (trimmed.Any(c => char.IsControl(c)))
            {
                return false;
            }

            var match = Defaults.FirstOrDefault(d =>
                string.Equals(trimmed, d, StringComparison.OrdinalIgnoreCase));
            normalized = match ?? trimmed;
            return true;
        }

        public static string NormalizeOrDefault(string? input, string fallback = Football) =>
            TryNormalize(input, out var normalized) ? normalized : fallback;
    }
}
