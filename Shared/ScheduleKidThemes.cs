using System;

using System.Collections.Generic;

using System.Linq;

using System.Text;



namespace BlazorApp.Shared

{

    /// <summary>

    /// Maps each schedule kid to a distinct table row CSS class (row-theme-kid-*).

    /// Hex colors below match Client schedule table cell styles.

    /// </summary>

    public static class ScheduleKidThemes

    {

        // Levi — baseball blue (bg #DBEAFE, border #94A3B8, text #1E3A8A)

        // Cohen — football green (bg #D8F3DC, border #52796F, text #1B4332)

        // June — soccer plum/pink (bg #FCE7F3, border #C4A8BD, text #3D2A35)

        // Cael — amber (bg #FEF3C7, border #D97706, text #78350F)

        // Lily — teal (bg #CFFAFE, border #0D9488, text #134E4A)

        // Silas — indigo/lavender (bg #E0E7FF, border #6366F1, text #312E81)



        public const string MultiRowClass = "row-theme-kids-multi";



        /// <summary>Width of each kid band in multi-kid diagonal stripes (~pinky width).</summary>

        public const int StripeWidthPx = 14;



        /// <summary>7:30 → 1:30 on a clock (bottom-left toward top-right).</summary>

        public const int StripeAngleDeg = 135;



        public static string RowThemeClass(string kid)

        {

            if (string.IsNullOrWhiteSpace(kid) || !ScheduleKids.IsAllowed(kid))

            {

                return string.Empty;

            }



            return $"row-theme-kid-{kid.Trim().ToLowerInvariant()}";

        }



        /// <summary>

        /// Combined schedule rows: when multiple kids are on an event, color uses

        /// <see cref="Event.RowThemeKid"/> (first allowed name alphabetically).

        /// </summary>

        public static string RowThemeClass(Event evt) => RowThemeClass(evt?.RowThemeKid ?? string.Empty);



        /// <summary>Allowed kids on the event, alphabetical (normalized list order).</summary>

        public static IReadOnlyList<string> ThemeKidsInOrder(Event? evt) =>

            ScheduleKids.NormalizeKidsList(evt?.Kids ?? Enumerable.Empty<string>())

                .Where(ScheduleKids.IsAllowed)

                .ToList();



        public static string GetBackgroundHex(string kid) => KidThemeColors.GetBackgroundHex(kid);

        /// <summary>Inline style for a kid name pill using assigned row background, text, and border colors.</summary>
        public static string GetKidBadgeInlineStyle(string kid)
        {
            if (string.IsNullOrWhiteSpace(kid) || !ScheduleKids.IsAllowed(kid))
            {
                return string.Empty;
            }

            var bg = GetBackgroundHex(kid);
            var text = KidThemeColors.GetTextHex(kid);
            var border = KidThemeColors.GetBorderHex(kid);
            return $"background-color:{bg};color:{text};border:1px solid {border};--schedule-kid-gutter:{border};";
        }

        /// <summary>

        /// Row class: single kid theme, <see cref="MultiRowClass"/> when 2+ kids, or empty.

        /// When no kids are on the event, <paramref name="defaultKidWhenNoKids"/> supplies a single-kid theme (sport pages).

        /// </summary>

        public static string GetRowClass(Event? evt, string? defaultKidWhenNoKids = null)

        {

            var kids = ThemeKidsInOrder(evt);

            if (kids.Count == 0)
            {
                if (defaultKidWhenNoKids is not { } fallback || string.IsNullOrWhiteSpace(fallback))
                {
                    return string.Empty;
                }

                return RowThemeClass(fallback.Trim());
            }



            if (kids.Count == 1)

            {

                return RowThemeClass(kids[0]!);

            }



            return MultiRowClass;

        }



        /// <summary>

        /// Inline style for multi-kid rows: sets <c>--row-stripe-bg</c> for CSS on <c>td</c>.

        /// </summary>

        public static string? GetRowStyle(Event? evt)

        {

            var kids = ThemeKidsInOrder(evt);

            if (kids.Count < 2)

            {

                return null;

            }



            return $"--row-stripe-bg: {BuildRepeatingStripeGradient(kids)};";

        }



        public static string BuildRepeatingStripeGradient(IReadOnlyList<string> kidsInOrder)

        {

            if (kidsInOrder.Count == 0)

            {

                return "none";

            }



            var stops = new StringBuilder();

            for (var i = 0; i < kidsInOrder.Count; i++)

            {

                var hex = GetBackgroundHex(kidsInOrder[i]);

                var start = i * StripeWidthPx;

                var end = (i + 1) * StripeWidthPx;

                if (stops.Length > 0)

                {

                    stops.Append(", ");

                }



                stops.Append(hex).Append(' ').Append(start).Append("px, ")

                    .Append(hex).Append(' ').Append(end).Append("px");

            }



            return $"repeating-linear-gradient({StripeAngleDeg}deg, {stops})";

        }



        public static string PageThemeClass(string kid)

        {

            if (string.IsNullOrWhiteSpace(kid) || !ScheduleKids.IsAllowed(kid))

            {

                return string.Empty;

            }



            return $"schedule-theme-kid-{kid.Trim().ToLowerInvariant()}";

        }

    }

}


