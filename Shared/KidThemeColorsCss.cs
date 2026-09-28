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

            sb.Append(scopeSelector)
                .Append(" .")
                .Append(ScheduleKidThemes.MultiRowClass)
                .Append(" td { background-color: transparent; background-image: var(--row-stripe-bg); border: 1px solid #94A3B8; color: #1F2937; }");

            return sb.ToString();
        }

        public static string BuildSportPageCss(string scopeSelector, string defaultKid)
        {
            var sb = new StringBuilder();
            foreach (var kid in ScheduleKids.AllowedKids)
            {
                AppendRowThemeRule(sb, scopeSelector, kid);
            }

            var bg = KidThemeColors.GetBackgroundHex(defaultKid);
            var text = KidThemeColors.GetTextHex(defaultKid);

            sb.Append(scopeSelector)
                .Append(" .schedule-table tbody tr td { background-color: ")
                .Append(bg)
                .Append("; color: ")
                .Append(text)
                .Append("; }");

            sb.Append(scopeSelector)
                .Append(" .schedule-table tbody tr.")
                .Append(ScheduleKidThemes.MultiRowClass)
                .Append(" td { background-color: transparent; background-image: var(--row-stripe-bg); color: #1F2937; }");

            return sb.ToString();
        }

        private static void AppendRowThemeRule(StringBuilder sb, string scopeSelector, string kid)
        {
            var className = ScheduleKidThemes.RowThemeClass(kid);
            if (string.IsNullOrEmpty(className))
            {
                return;
            }

            sb.Append(scopeSelector)
                .Append(" .")
                .Append(className)
                .Append(" td { background-color: ")
                .Append(KidThemeColors.GetBackgroundHex(kid))
                .Append("; border: 1px solid ")
                .Append(KidThemeColors.GetBorderHex(kid))
                .Append("; color: ")
                .Append(KidThemeColors.GetTextHex(kid))
                .Append("; }");
        }
    }
}
