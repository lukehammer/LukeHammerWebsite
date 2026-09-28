using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorApp.Shared
{
    public static class ScheduleKids
    {
        public static readonly IReadOnlyList<string> AllowedKids = new[]
        {
            "Cael",
            "Cohen",
            "June",
            "Levi",
            "Lily",
            "Silas"
        };

        public static bool IsAllowed(string kid)
        {
            if (string.IsNullOrWhiteSpace(kid))
            {
                return false;
            }

            var canonical = CanonicalizeKidName(kid);
            return AllowedKids.Any(allowed =>
                string.Equals(canonical, allowed, StringComparison.Ordinal));
        }

        /// <summary>Maps legacy misspellings to the canonical kid name.</summary>
        public static string CanonicalizeKidName(string kid)
        {
            var trimmed = kid?.Trim() ?? string.Empty;
            if (string.Equals(trimmed, "Choen", StringComparison.Ordinal))
            {
                return "Cohen";
            }

            return trimmed;
        }

        public static List<string> NormalizeKidsList(IEnumerable<string> kids)
        {
            if (kids == null)
            {
                return new List<string>();
            }

            return kids
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(CanonicalizeKidName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();
        }

        public static bool TryValidateKids(IEnumerable<string> kids, out string errorMessage)
        {
            var normalized = NormalizeKidsList(kids);
            if (normalized.Count == 0)
            {
                errorMessage = "At least one kid is required.";
                return false;
            }

            foreach (var kid in normalized)
            {
                if (!IsAllowed(kid))
                {
                    errorMessage =
                        $"Invalid kid '{kid}'. Allowed values: {string.Join(", ", AllowedKids)}.";
                    return false;
                }
            }

            errorMessage = string.Empty;
            return true;
        }
    }
}
