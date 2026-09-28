using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorApp.Shared
{
    public static class ScheduleSubmitters
    {
        /// <summary>Secret sign-in code mapped to Luke (not advertised in UI).</summary>
        public const string LukeAuthCode = "1123";

        public const string FakeLukeMessage = "ha ha ha I know you are not Luke";

        public static readonly IReadOnlyList<string> AllowedAdults = new[]
        {
            "Alaina",
            "Leneigh",
            "Stevie",
            "Laura",
            "Steve",
            "Casey"
        };

        public static IReadOnlyList<string> AllowedFirstNames { get; } =
            AllowedAdults
                .Concat(ScheduleKids.AllowedKids)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

        public const string InvalidSessionMessage =
            "Your sign-in is no longer valid. Sign out and sign in again.";

        public static bool IsAllowedFirstName(string submittedBy) =>
            TryResolveSubmitterForWrite(submittedBy, out _, out _);

        /// <summary>
        /// Trims input, rejects multi-word names, and returns the canonical allowlist spelling when valid.
        /// </summary>
        public static string? NormalizeFirstName(string submittedBy)
        {
            return TryResolveSubmitterForWrite(submittedBy, out var normalized, out _)
                ? normalized
                : null;
        }

        /// <summary>Login form only — rejects the name &quot;Luke&quot; with the joke message.</summary>
        public static bool TryValidateLoginFirstName(
            string submittedBy,
            out string normalized,
            out string errorMessage)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(submittedBy))
            {
                errorMessage = "Your first name is required.";
                return false;
            }

            var trimmed = submittedBy.Trim();
            if (ContainsWhitespace(trimmed))
            {
                errorMessage = FormatNameNotAllowedMessage(trimmed);
                return false;
            }

            if (string.Equals(trimmed, "Luke", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = FakeLukeMessage;
                return false;
            }

            if (string.Equals(trimmed, LukeAuthCode, StringComparison.Ordinal))
            {
                normalized = "Luke";
                errorMessage = string.Empty;
                return true;
            }

            if (!TryMatchAllowedFirstName(trimmed, out var loginMatch))
            {
                errorMessage = FormatNameNotAllowedMessage(trimmed);
                return false;
            }

            normalized = loginMatch;
            errorMessage = string.Empty;
            return true;
        }

        /// <summary>Signed-in writes (add/edit/delete, colors). Never returns <see cref="FakeLukeMessage"/>.</summary>
        public static bool TryResolveSubmitterForWrite(
            string submittedBy,
            out string normalized,
            out string errorMessage)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(submittedBy))
            {
                errorMessage = InvalidSessionMessage;
                return false;
            }

            var trimmed = submittedBy.Trim();
            if (ContainsWhitespace(trimmed))
            {
                errorMessage = InvalidSessionMessage;
                return false;
            }

            if (string.Equals(trimmed, LukeAuthCode, StringComparison.Ordinal))
            {
                normalized = "Luke";
                errorMessage = string.Empty;
                return true;
            }

            if (string.Equals(trimmed, "Luke", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "Luke";
                errorMessage = string.Empty;
                return true;
            }

            if (!TryMatchAllowedFirstName(trimmed, out var writeMatch))
            {
                errorMessage = InvalidSessionMessage;
                return false;
            }

            normalized = writeMatch;
            errorMessage = string.Empty;
            return true;
        }

        public static bool TryValidateFirstName(
            string submittedBy,
            out string normalized,
            out string errorMessage) =>
            TryValidateLoginFirstName(submittedBy, out normalized, out errorMessage);

        private static bool TryMatchAllowedFirstName(string trimmed, out string match)
        {
            match = AllowedFirstNames.FirstOrDefault(allowed =>
                string.Equals(trimmed, allowed, StringComparison.OrdinalIgnoreCase))
                ?? string.Empty;

            if (!string.IsNullOrEmpty(match))
            {
                return true;
            }

            var canonicalKid = ScheduleKids.CanonicalizeKidName(trimmed);
            match = AllowedFirstNames.FirstOrDefault(allowed =>
                string.Equals(canonicalKid, allowed, StringComparison.Ordinal))
                ?? string.Empty;

            return !string.IsNullOrEmpty(match);
        }

        private static bool ContainsWhitespace(string value) =>
            value.Any(char.IsWhiteSpace);

        private static string FormatNameNotAllowedMessage(string enteredName)
        {
            if (string.IsNullOrWhiteSpace(enteredName))
            {
                return "That name is not allowed.";
            }

            var display = enteredName.Trim();
            if (display.Length == 1)
            {
                display = display.ToUpperInvariant();
            }
            else
            {
                display = char.ToUpperInvariant(display[0]) + display.Substring(1).ToLowerInvariant();
            }

            return $"{display} is not allowed.";
        }
    }
}
