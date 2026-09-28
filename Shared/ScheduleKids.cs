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
            "Choen",
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

            var trimmed = kid.Trim();
            return AllowedKids.Any(allowed =>
                string.Equals(trimmed, allowed, StringComparison.Ordinal));
        }

        public static List<string> NormalizeKidsList(IEnumerable<string> kids)
        {
            if (kids == null)
            {
                return new List<string>();
            }

            return kids
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
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
