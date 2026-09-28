using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorApp.Shared
{
    /// <summary>
    /// Identifies schedule edits made by Luke (no notification). Default: Luke (case-insensitive).
    /// Override with comma-separated LUKE_NAME_ALIASES in app settings.
    /// </summary>
    public static class LukeNameMatcher
    {
        public static readonly IReadOnlyList<string> DefaultLukeAliases = new[]
        {
            "Luke"
        };

        public static bool IsLuke(string submittedBy, IEnumerable<string> configuredAliases = null)
        {
            if (string.IsNullOrWhiteSpace(submittedBy))
            {
                return false;
            }

            var trimmed = submittedBy.Trim();
            var aliases = (configuredAliases ?? DefaultLukeAliases)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim());

            return aliases.Any(alias =>
                string.Equals(trimmed, alias, StringComparison.OrdinalIgnoreCase));
        }

        public static IReadOnlyList<string> ParseAliasesFromConfig(string commaSeparated) =>
            string.IsNullOrWhiteSpace(commaSeparated)
                ? DefaultLukeAliases
                : commaSeparated.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToList();
    }
}
