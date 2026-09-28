using BlazorApp.Shared;

namespace BlazorApp.Client
{
    public static class KidThemeColorsLoader
    {
        public static void Apply(KidThemeColorsData data)
        {
            KidThemeColors.ApplyLoaded(data);
        }

        public static string CombinedPageCss() =>
            KidThemeColorsCss.BuildCombinedScopeCss(".schedule-theme-combined");

        public static string SportPageCss(string scopeClass, string defaultKid) =>
            KidThemeColorsCss.BuildSportPageCss(scopeClass, defaultKid);
    }
}
