using System.Text;

namespace BlazorApp.Shared
{
    public static class KidThemeColorsCss
    {
        public static string BuildCombinedScopeCss(string scopeSelector)
        {
            var sb = new StringBuilder();
            foreach (var kid in ScheduleKids.AllowedKids)
            {
                AppendRowThemeRule(sb, scopeSelector, kid);
            }

            AppendMultiRowThemeRule(sb, scopeSelector);

            AppendMobileUnifiedRowBackgroundCss(sb, scopeSelector);

            return sb.ToString();
        }

        public static string BuildSportPageCss(string scopeSelector, string defaultKid)
        {
            var sb = new StringBuilder();
            foreach (var kid in ScheduleKids.AllowedKids)
            {
                AppendRowThemeRule(sb, scopeSelector, kid);
            }

            AppendMultiRowThemeRule(sb, scopeSelector);

            var bg = KidThemeColors.GetBackgroundHex(defaultKid);
            var text = KidThemeColors.GetTextHex(defaultKid);
            sb.Append(scopeSelector)
                .Append(" .schedule-table-responsive tbody tr.schedule-data-row:not([class*=\"row-theme-kid-\"]):not(.")
                .Append(ScheduleKidThemes.MultiRowClass)
                .Append(") { background-color: ")
                .Append(bg)
                .Append("; color: ")
                .Append(text)
                .Append("; }");

            AppendMobileUnifiedRowBackgroundCss(sb, scopeSelector);

            return sb.ToString();
        }

        private static void AppendRowThemeRule(StringBuilder sb, string scopeSelector, string kid)
        {
            var className = ScheduleKidThemes.RowThemeClass(kid);
            if (string.IsNullOrEmpty(className))
            {
                return;
            }

            var bg = KidThemeColors.GetBackgroundHex(kid);
            var text = KidThemeColors.GetTextHex(kid);

            sb.Append(scopeSelector)
                .Append(" .schedule-table-responsive tbody tr.schedule-data-row.")
                .Append(className)
                .Append(" { background-color: ")
                .Append(bg)
                .Append("; color: ")
                .Append(text)
                .Append("; }");

            sb.Append(scopeSelector)
                .Append(" .")
                .Append(className)
                .Append(" td { background-color: transparent; background-image: none; }");
        }

        private static void AppendMultiRowThemeRule(StringBuilder sb, string scopeSelector)
        {
            sb.Append(scopeSelector)
                .Append(" .schedule-table-responsive tbody tr.schedule-data-row.")
                .Append(ScheduleKidThemes.MultiRowClass)
                .Append(" { background-color: transparent; background-image: var(--row-stripe-bg); color: #1F2937; }");

            sb.Append(scopeSelector)
                .Append(" .")
                .Append(ScheduleKidThemes.MultiRowClass)
                .Append(" td { background-color: transparent; background-image: none; }");
        }

        /// <summary>
        /// Mobile card layout: keep content cells clear so the row-level stripe shows through.
        /// </summary>
        private static void AppendMobileUnifiedRowBackgroundCss(StringBuilder sb, string scopeSelector)
        {
            sb.Append("@media (max-width: 640px) { ");

            sb.Append(scopeSelector)
                .Append(" .schedule-table-responsive tbody tr.schedule-data-row td.schedule-cell-when,")
                .Append(scopeSelector)
                .Append(" .schedule-table-responsive tbody tr.schedule-data-row td.schedule-cell-matchup,")
                .Append(scopeSelector)
                .Append(" .schedule-table-responsive tbody tr.schedule-data-row td.schedule-cell-location ")
                .Append("{ background-color: transparent !important; background-image: none !important; } ");

            sb.Append('}');
        }
    }
}
