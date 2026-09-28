using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlazorApp.Shared
{
    public static class KidThemeColors
    {
        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static readonly Regex HexPattern = new(@"^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

        private static IReadOnlyDictionary<string, string> _backgroundOverrides =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyDictionary<string, string> DefaultBackgroundByKid { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Levi"] = "#DBEAFE",
                ["Choen"] = "#D8F3DC",
                ["June"] = "#FBCFE8",
                ["Cael"] = "#FEF3C7",
                ["Lily"] = "#CFFAFE",
                ["Silas"] = "#E9D5FF",
            };

        public static IReadOnlyDictionary<string, string> DefaultBorderByKid { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Levi"] = "#94A3B8",
                ["Choen"] = "#52796F",
                ["June"] = "#C4A8BD",
                ["Cael"] = "#D97706",
                ["Lily"] = "#0D9488",
                ["Silas"] = "#6366F1",
            };

        public static IReadOnlyDictionary<string, string> DefaultTextByKid { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Levi"] = "#1E3A8A",
                ["Choen"] = "#1B4332",
                ["June"] = "#3D2A35",
                ["Cael"] = "#78350F",
                ["Lily"] = "#134E4A",
                ["Silas"] = "#312E81",
            };

        public static void ApplyLoaded(KidThemeColorsData? data)
        {
            if (data?.BackgroundByKid == null || data.BackgroundByKid.Count == 0)
            {
                _backgroundOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            var merged = MergeWithDefaults(data.BackgroundByKid);
            _backgroundOverrides = merged;
        }

        public static KidThemeColorsData CreateDefaultData()
        {
            return new KidThemeColorsData
            {
                BackgroundByKid = ScheduleKids.AllowedKids.ToDictionary(
                    k => k,
                    k => DefaultBackgroundByKid[k],
                    StringComparer.Ordinal)
            };
        }

        public static KidThemeColorsData ParseJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return CreateDefaultData();
            }

            var data = JsonSerializer.Deserialize<KidThemeColorsData>(json, JsonOptions)
                ?? new KidThemeColorsData();

            data.BackgroundByKid = MergeWithDefaults(data.BackgroundByKid);
            return data;
        }

        public static string ToJson(KidThemeColorsData data)
        {
            var normalized = new KidThemeColorsData
            {
                BackgroundByKid = MergeWithDefaults(data.BackgroundByKid)
            };

            return JsonSerializer.Serialize(normalized, JsonOptions);
        }

        public static bool TryParseJson(string json, out KidThemeColorsData data, out string? errorMessage)
        {
            data = CreateDefaultData();
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                return true;
            }

            try
            {
                data = ParseJson(json);
                return true;
            }
            catch (JsonException)
            {
                errorMessage = "Kid theme colors are invalid or corrupted.";
                return false;
            }
        }

        public static bool TryValidateWrite(KidThemeColorsData colors, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (colors?.BackgroundByKid == null || colors.BackgroundByKid.Count == 0)
            {
                errorMessage = "backgroundByKid is required.";
                return false;
            }

            foreach (var kid in ScheduleKids.AllowedKids)
            {
                if (!colors.BackgroundByKid.TryGetValue(kid, out var hex) || string.IsNullOrWhiteSpace(hex))
                {
                    errorMessage = $"Background color for {kid} is required.";
                    return false;
                }

                if (!IsValidHex(hex.Trim()))
                {
                    errorMessage = $"Invalid color for {kid}. Use #RRGGBB format.";
                    return false;
                }
            }

            foreach (var pair in colors.BackgroundByKid)
            {
                if (!ScheduleKids.IsAllowed(pair.Key))
                {
                    errorMessage = $"Unknown kid '{pair.Key}'.";
                    return false;
                }
            }

            if (!TryValidateColorRules(colors, out var issues))
            {
                errorMessage = FormatColorRuleError(issues);
                return false;
            }

            return true;
        }

        public static bool TryValidateColorRules(
            KidThemeColorsData colors,
            out IReadOnlyDictionary<string, KidThemeColorValidationIssue> issuesByKid)
        {
            issuesByKid = KidColorSuggestions.ValidateAllBackgrounds(colors.BackgroundByKid);
            return issuesByKid.Count == 0;
        }

        public static string FormatColorRuleError(IReadOnlyDictionary<string, KidThemeColorValidationIssue> issuesByKid)
        {
            if (issuesByKid.Count == 0)
            {
                return string.Empty;
            }

            var first = issuesByKid.First();
            var msg = $"{first.Key}: {first.Value.Message}";
            if (!string.IsNullOrWhiteSpace(first.Value.SuggestedHex))
            {
                msg += $" Try {first.Value.SuggestedHex}.";
            }

            if (issuesByKid.Count > 1)
            {
                msg += $" ({issuesByKid.Count} kids need color changes.)";
            }

            return msg;
        }

        public static string GetBackgroundHex(string kid)
        {
            if (string.IsNullOrWhiteSpace(kid))
            {
                return "#F3F4F6";
            }

            var trimmed = kid.Trim();
            if (_backgroundOverrides.TryGetValue(trimmed, out var loaded))
            {
                return loaded;
            }

            return DefaultBackgroundByKid.TryGetValue(trimmed, out var hex) ? hex : "#F3F4F6";
        }

        public static string GetBorderHex(string kid)
        {
            var trimmed = kid?.Trim() ?? string.Empty;
            return DefaultBorderByKid.TryGetValue(trimmed, out var hex) ? hex : "#94A3B8";
        }

        public static string GetTextHex(string kid)
        {
            var trimmed = kid?.Trim() ?? string.Empty;
            return DefaultTextByKid.TryGetValue(trimmed, out var hex) ? hex : "#1F2937";
        }

        public static bool IsValidHex(string hex) => HexPattern.IsMatch(hex);

        private static Dictionary<string, string> MergeWithDefaults(Dictionary<string, string>? incoming)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kid in ScheduleKids.AllowedKids)
            {
                if (incoming != null
                    && incoming.TryGetValue(kid, out var hex)
                    && !string.IsNullOrWhiteSpace(hex)
                    && IsValidHex(hex.Trim()))
                {
                    result[kid] = hex.Trim();
                }
                else
                {
                    result[kid] = DefaultBackgroundByKid[kid];
                }
            }

            return result;
        }
    }
}
