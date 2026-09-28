using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorApp.Shared
{
    public class ScheduleSpectatorsData
    {
        [JsonPropertyName("customNames")]
        public List<string> CustomNames { get; set; } = new();
    }

    public static class ScheduleSpectators
    {
        public const int MaxNameLength = 40;

        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        /// <summary>Default picker entries: all kids plus adults who can edit schedules.</summary>
        public static IReadOnlyList<string> BuiltInNames { get; } =
            ScheduleKids.AllowedKids
                .Concat(ScheduleSubmitters.AllowedAdults)
                .Append("Luke")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

        public static ScheduleSpectatorsData ParseJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ScheduleSpectatorsData();
            }

            try
            {
                return JsonSerializer.Deserialize<ScheduleSpectatorsData>(json, JsonOptions)
                       ?? new ScheduleSpectatorsData();
            }
            catch (JsonException)
            {
                return new ScheduleSpectatorsData();
            }
        }

        public static string ToJson(ScheduleSpectatorsData data) =>
            JsonSerializer.Serialize(data ?? new ScheduleSpectatorsData(), JsonOptions);

        public static IReadOnlyList<string> MergeNames(ScheduleSpectatorsData? data)
        {
            var custom = data?.CustomNames ?? new List<string>();
            var merged = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in BuiltInNames.Concat(custom))
            {
                if (!TryNormalizeName(name, out var normalized))
                {
                    continue;
                }

                if (seen.Add(normalized))
                {
                    merged.Add(normalized);
                }
            }

            return merged.OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        public static ScheduleSpectatorsData MergeCustomNames(
            ScheduleSpectatorsData data,
            IEnumerable<string> newNames)
        {
            data ??= new ScheduleSpectatorsData();
            data.CustomNames ??= new List<string>();

            var mergedCustom = new List<string>(data.CustomNames);
            var seenBuiltIn = new HashSet<string>(
                BuiltInNames.Select(n => n),
                StringComparer.OrdinalIgnoreCase);
            var seenCustom = new HashSet<string>(
                mergedCustom,
                StringComparer.OrdinalIgnoreCase);

            foreach (var raw in newNames ?? Array.Empty<string>())
            {
                if (!TryNormalizeName(raw, out var normalized))
                {
                    continue;
                }

                if (seenBuiltIn.Contains(normalized))
                {
                    continue;
                }

                if (seenCustom.Add(normalized))
                {
                    mergedCustom.Add(normalized);
                }
            }

            data.CustomNames = mergedCustom
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            return data;
        }

        public static bool TryNormalizeName(string? raw, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var trimmed = raw.Trim();
            if (trimmed.Length > MaxNameLength)
            {
                return false;
            }

            if (trimmed.Any(char.IsControl))
            {
                return false;
            }

            var builtIn = BuiltInNames.FirstOrDefault(n =>
                string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(builtIn))
            {
                normalized = builtIn;
                return true;
            }

            if (ScheduleSubmitters.NormalizeFirstName(trimmed) is { } submitter)
            {
                normalized = submitter;
                return true;
            }

            var canonicalKid = ScheduleKids.CanonicalizeKidName(trimmed);
            if (ScheduleKids.IsAllowed(canonicalKid))
            {
                normalized = canonicalKid;
                return true;
            }

            normalized = ToTitleWords(trimmed);
            return !string.IsNullOrWhiteSpace(normalized);
        }

        private static string ToTitleWords(string value)
        {
            var parts = value.Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                return string.Empty;
            }

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part.Length == 1)
                {
                    parts[i] = part.ToUpperInvariant();
                }
                else
                {
                    parts[i] = char.ToUpperInvariant(part[0]) + part.Substring(1).ToLowerInvariant();
                }
            }

            return string.Join(" ", parts);
        }
    }
}
