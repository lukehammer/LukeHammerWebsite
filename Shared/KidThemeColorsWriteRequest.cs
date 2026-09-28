namespace BlazorApp.Shared
{
    public class KidThemeColorsWriteRequest
    {
        public string SubmittedBy { get; set; } = string.Empty;
        public KidThemeColorsData Colors { get; set; } = new();
    }
}
