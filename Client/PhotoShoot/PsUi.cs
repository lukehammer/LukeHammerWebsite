using BlazorApp.Shared.PhotoShoot;

namespace BlazorApp.Client.PhotoShoot
{
    /// <summary>Small presentation helpers shared by the photo shoot pages.</summary>
    public static class PsUi
    {
        public static string StatusClass(ShotStatus status) => status switch
        {
            ShotStatus.Completed => "ps-status-done",
            ShotStatus.InProgress => "ps-status-active",
            ShotStatus.Skipped => "ps-status-skipped",
            _ => "ps-status-todo"
        };

        public static string StatusIcon(ShotStatus status) => status switch
        {
            ShotStatus.Completed => "bi-check-circle-fill",
            ShotStatus.InProgress => "bi-camera-fill",
            ShotStatus.Skipped => "bi-skip-forward-circle",
            _ => "bi-circle"
        };

        public static string PriorityClass(ShotPriority priority) => priority switch
        {
            ShotPriority.MustHave => "ps-pri-must",
            ShotPriority.Bonus => "ps-pri-bonus",
            _ => "ps-pri-standard"
        };

        public static string Minutes(int minutes) => minutes <= 0 ? "—" : $"{minutes} min";

        public static string Elapsed(DateTime? sinceUtc)
        {
            if (sinceUtc == null)
            {
                return "";
            }

            var span = DateTime.UtcNow - sinceUtc.Value;
            if (span < TimeSpan.Zero)
            {
                span = TimeSpan.Zero;
            }

            return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
        }

        public static string LocalTime(DateTime? utc) =>
            utc == null ? "" : utc.Value.ToLocalTime().ToString("h:mm tt");

        public static string ImageSearchUrl(string phrase) =>
            "https://www.google.com/search?tbm=isch&q=" + Uri.EscapeDataString(phrase ?? "");

        public static string Join(IEnumerable<string>? items) =>
            items == null ? "" : string.Join(", ", items.Where(i => !string.IsNullOrWhiteSpace(i)));

        public static bool Has(string? text) => !string.IsNullOrWhiteSpace(text);

        public static string ShotUrl(PhotoShot shot) => $"photoshoot/shots/{shot.Id}";

        public static string SetupLabel(PhotoShootDocument doc, PhotoShot shot)
        {
            var setup = PhotoShootPlanner.FindSetup(doc, shot.SetupId);
            return setup == null ? "Unassigned" : $"Setup {setup.Sequence}: {setup.Name}";
        }
    }
}
