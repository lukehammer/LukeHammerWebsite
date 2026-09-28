using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BlazorApp.Shared
{
    public static class KidColorSuggestions
    {
        /// <summary>CIE76 Delta E threshold; colors closer than this are considered too similar.</summary>
        public const double DefaultSimilarityThreshold = 12.0;

        /// <summary>
        /// WCAG 2.1 Level AA — Success Criterion 1.4.3 Contrast (Minimum), normal text.
        /// See https://www.w3.org/WAI/WCAG21/Understanding/contrast-minimum.html
        /// (ADA-aligned accessibility practice commonly targets this ratio via Section 508).
        /// </summary>
        public const double Wcag21AaNormalTextMinContrastRatio = 4.5;

        /// <summary>Foreground used when validating schedule row readability (black text).</summary>
        public const string ScheduleRowTextHex = "#000000";

        private const int GrayChannelSpreadMax = 18;
        private const int NearWhiteChannelMin = 245;
        private const int NearBlackChannelMax = 12;

        private static readonly string[] VividHueCandidates = BuildVividHueCandidates();

        public static bool IsMonochromeForbidden(string hex)
        {
            if (!TryParseRgb(hex, out var r, out var g, out var b))
            {
                return true;
            }

            if (r <= NearBlackChannelMax && g <= NearBlackChannelMax && b <= NearBlackChannelMax)
            {
                return true;
            }

            if (r >= NearWhiteChannelMin && g >= NearWhiteChannelMin && b >= NearWhiteChannelMin)
            {
                return true;
            }

            var spread = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            return spread <= GrayChannelSpreadMax;
        }

        /// <summary>WCAG 2.1 contrast ratio between two sRGB colors (#RRGGBB).</summary>
        public static double ContrastRatio(string foregroundHex, string backgroundHex)
        {
            var lText = RelativeLuminance(foregroundHex);
            var lBg = RelativeLuminance(backgroundHex);
            var lighter = Math.Max(lText, lBg);
            var darker = Math.Min(lText, lBg);
            return (lighter + 0.05) / (darker + 0.05);
        }

        public static bool MeetsWcag21AaNormalTextContrast(
            string backgroundHex,
            string foregroundHex = ScheduleRowTextHex)
        {
            if (!TryParseRgb(backgroundHex, out _, out _, out _)
                || !TryParseRgb(foregroundHex, out _, out _, out _))
            {
                return false;
            }

            return ContrastRatio(foregroundHex, backgroundHex) >= Wcag21AaNormalTextMinContrastRatio;
        }

        public static bool IsTooSimilar(string hex, string otherHex, double threshold)
        {
            if (string.Equals(NormalizeHex(hex), NormalizeHex(otherHex), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return DeltaE(hex, otherHex) < threshold;
        }

        public static double DeltaE(string hexA, string hexB)
        {
            if (!TryParseRgb(hexA, out var r1, out var g1, out var b1)
                || !TryParseRgb(hexB, out var r2, out var g2, out var b2))
            {
                return double.MaxValue;
            }

            var lab1 = RgbToLab(r1, g1, b1);
            var lab2 = RgbToLab(r2, g2, b2);

            var dL = lab1.L - lab2.L;
            var da = lab1.A - lab2.A;
            var db = lab1.B - lab2.B;
            return Math.Sqrt(dL * dL + da * da + db * db);
        }

        public static string? SuggestNextColor(IEnumerable<string> alreadyUsed)
        {
            var used = alreadyUsed
                .Select(NormalizeHex)
                .Where(h => KidThemeColors.IsValidHex(h))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var candidates = CollectCandidates()
                .Where(c => !IsMonochromeForbidden(c))
                .Where(c => MeetsWcag21AaNormalTextContrast(c))
                .Where(c => !used.Any(u => IsTooSimilar(c, u, DefaultSimilarityThreshold)))
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            string? best = null;
            var bestScore = double.NegativeInfinity;

            foreach (var candidate in candidates)
            {
                var minDist = used.Count == 0
                    ? double.MaxValue
                    : used.Min(u => DeltaE(candidate, u));

                if (minDist > bestScore)
                {
                    bestScore = minDist;
                    best = candidate;
                }
            }

            return best;
        }

        public static IReadOnlyDictionary<string, KidThemeColorValidationIssue> ValidateAllBackgrounds(
            IReadOnlyDictionary<string, string> backgroundByKid)
        {
            var issues = new Dictionary<string, KidThemeColorValidationIssue>(StringComparer.Ordinal);

            foreach (var kid in ScheduleKids.AllowedKids)
            {
                if (!backgroundByKid.TryGetValue(kid, out var hex) || string.IsNullOrWhiteSpace(hex))
                {
                    continue;
                }

                var normalized = NormalizeHex(hex.Trim());
                var others = ScheduleKids.AllowedKids
                    .Where(k => !string.Equals(k, kid, StringComparison.Ordinal))
                    .Select(k => backgroundByKid.TryGetValue(k, out var h) ? NormalizeHex(h?.Trim() ?? "") : "")
                    .Where(h => KidThemeColors.IsValidHex(h))
                    .ToList();

                var issue = ValidateSingleColor(normalized, others);
                if (issue != null)
                {
                    issues[kid] = issue;
                }
            }

            return issues;
        }

        public static KidThemeColorValidationIssue? ValidateSingleColor(
            string hex,
            IReadOnlyList<string> otherUsedHexes)
        {
            if (!KidThemeColors.IsValidHex(hex))
            {
                var used = otherUsedHexes.Where(h => KidThemeColors.IsValidHex(h)).ToList();
                return new KidThemeColorValidationIssue
                {
                    Message = "Use #RRGGBB format.",
                    SuggestedHex = SuggestNextColor(used)
                };
            }

            var normalized = NormalizeHex(hex);
            var others = otherUsedHexes
                .Select(NormalizeHex)
                .Where(h => KidThemeColors.IsValidHex(h))
                .ToList();

            if (IsMonochromeForbidden(normalized))
            {
                return new KidThemeColorValidationIssue
                {
                    Message = "White, black, and gray tones are not allowed. Pick a more vivid color.",
                    SuggestedHex = SuggestNextColor(others)
                };
            }

            if (!MeetsWcag21AaNormalTextContrast(normalized))
            {
                var ratio = ContrastRatio(ScheduleRowTextHex, normalized);
                return new KidThemeColorValidationIssue
                {
                    Message =
                        $"Contrast with black text is too low ({ratio:F1}:1). WCAG 2.1 AA requires at least {Wcag21AaNormalTextMinContrastRatio:F1}:1 for normal text.",
                    SuggestedHex = SuggestNextColor(others)
                };
            }

            foreach (var other in others)
            {
                if (string.Equals(normalized, other, StringComparison.OrdinalIgnoreCase))
                {
                    return new KidThemeColorValidationIssue
                    {
                        Message = "This color is already used for another kid.",
                        SuggestedHex = SuggestNextColor(others)
                    };
                }

                if (IsTooSimilar(normalized, other, DefaultSimilarityThreshold))
                {
                    return new KidThemeColorValidationIssue
                    {
                        Message = "This color is too similar to another kid's color.",
                        SuggestedHex = SuggestNextColor(others)
                    };
                }
            }

            return null;
        }

        private static IEnumerable<string> CollectCandidates()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kid in ScheduleKids.AllowedKids)
            {
                if (KidThemeColors.DefaultBackgroundByKid.TryGetValue(kid, out var hex)
                    && seen.Add(NormalizeHex(hex)))
                {
                    yield return NormalizeHex(hex);
                }
            }

            foreach (var hex in VividHueCandidates)
            {
                if (seen.Add(hex))
                {
                    yield return hex;
                }
            }
        }

        private static string[] BuildVividHueCandidates()
        {
            var list = new List<string>();
            for (var hue = 0; hue < 360; hue += 15)
            {
                var hex = HslToHex(hue, 0.62, 0.78);
                list.Add(hex);
            }

            for (var hue = 0; hue < 360; hue += 15)
            {
                var hex = HslToHex(hue, 0.55, 0.68);
                list.Add(hex);
            }

            return list.ToArray();
        }

        private static string NormalizeHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return hex;
            }

            var trimmed = hex.Trim();
            if (!trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                trimmed = "#" + trimmed;
            }

            if (trimmed.Length == 4)
            {
                return $"#{trimmed[1]}{trimmed[1]}{trimmed[2]}{trimmed[2]}{trimmed[3]}{trimmed[3]}".ToUpperInvariant();
            }

            return trimmed.ToUpperInvariant();
        }

        private static double RelativeLuminance(string hex)
        {
            if (!TryParseRgb(hex, out var r, out var g, out var b))
            {
                return 0;
            }

            static double Linearize(int channel)
            {
                var s = channel / 255.0;
                return s <= 0.03928
                    ? s / 12.92
                    : Math.Pow((s + 0.055) / 1.055, 2.4);
            }

            var rr = Linearize(r);
            var gg = Linearize(g);
            var bb = Linearize(b);
            return 0.2126 * rr + 0.7152 * gg + 0.0722 * bb;
        }

        private static bool TryParseRgb(string hex, out int r, out int g, out int b)
        {
            r = g = b = 0;
            var normalized = NormalizeHex(hex);
            if (!KidThemeColors.IsValidHex(normalized))
            {
                return false;
            }

            r = int.Parse(normalized.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            g = int.Parse(normalized.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            b = int.Parse(normalized.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return true;
        }

        private static (double L, double A, double B) RgbToLab(int r, int g, int b)
        {
            static double Pivot(double n)
            {
                n /= 255.0;
                return n > 0.04045
                    ? Math.Pow((n + 0.055) / 1.055, 2.4)
                    : n / 12.92;
            }

            var rr = Pivot(r);
            var gg = Pivot(g);
            var bb = Pivot(b);

            var x = (rr * 0.4124564 + gg * 0.3575761 + bb * 0.1804375) / 0.95047;
            var y = (rr * 0.2126729 + gg * 0.7151522 + bb * 0.0721750) / 1.00000;
            var z = (rr * 0.0193339 + gg * 0.1191920 + bb * 0.9503041) / 1.08883;

            static double F(double t) =>
                t > 0.008856 ? Math.Pow(t, 1.0 / 3.0) : (7.787 * t) + (16.0 / 116.0);

            var fx = F(x);
            var fy = F(y);
            var fz = F(z);

            var L = (116.0 * fy) - 16.0;
            var A = 500.0 * (fx - fy);
            var B = 200.0 * (fy - fz);
            return (L, A, B);
        }

        private static string HslToHex(int hueDegrees, double saturation, double lightness)
        {
            var h = hueDegrees / 360.0;
            var s = saturation;
            var l = lightness;

            double r, g, b;
            if (s <= 0.00001)
            {
                r = g = b = l;
            }
            else
            {
                static double HueToRgb(double p, double q, double t)
                {
                    if (t < 0)
                    {
                        t += 1;
                    }

                    if (t > 1)
                    {
                        t -= 1;
                    }

                    if (t < 1.0 / 6.0)
                    {
                        return p + (q - p) * 6.0 * t;
                    }

                    if (t < 1.0 / 2.0)
                    {
                        return q;
                    }

                    if (t < 2.0 / 3.0)
                    {
                        return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
                    }

                    return p;
                }

                var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                var p = 2 * l - q;
                r = HueToRgb(p, q, h + 1.0 / 3.0);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1.0 / 3.0);
            }

            var ri = (int)Math.Round(r * 255);
            var gi = (int)Math.Round(g * 255);
            var bi = (int)Math.Round(b * 255);
            return $"#{ri:X2}{gi:X2}{bi:X2}";
        }
    }
}
