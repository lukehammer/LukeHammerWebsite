using System.Collections.Generic;

namespace BlazorApp.Shared
{
    public class KidThemeColorsData
    {
        /// <summary>Background hex (#RRGGBB) per allowed kid name.</summary>
        public Dictionary<string, string> BackgroundByKid { get; set; } = new();
    }
}
